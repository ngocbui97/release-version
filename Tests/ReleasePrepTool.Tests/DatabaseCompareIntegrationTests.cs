using NUnit.Framework;
using Npgsql;
using ReleasePrepTool.Models;
using ReleasePrepTool.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    [Category("Integration")]
    public class DatabaseCompareIntegrationTests
    {
        private const string Host = "localhost";
        private const int Port = 5432;
        private const string Username = "postgres";
        private const string Password = "B@sebs1234%";
        
        private const string SrcDbName = "lcm_integration_src";
        private const string TgtDbName = "lcm_integration_tgt";

        private DatabaseConfig _srcConfig = default!;
        private DatabaseConfig _tgtConfig = default!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _srcConfig = new DatabaseConfig { Host = Host, Port = Port, Username = Username, Password = Password, DatabaseName = SrcDbName };
            _tgtConfig = new DatabaseConfig { Host = Host, Port = Port, Username = Username, Password = Password, DatabaseName = TgtDbName };

            await DropDatabaseIfExists(SrcDbName);
            await DropDatabaseIfExists(TgtDbName);
            await CreateDatabase(SrcDbName);
            await CreateDatabase(TgtDbName);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await DropDatabaseIfExists(SrcDbName);
            await DropDatabaseIfExists(TgtDbName);
        }

        private async Task CreateDatabase(string dbName)
        {
            var connStr = $"Host={Host};Port={Port};Username={Username};Password={Password};Database=postgres";
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task DropDatabaseIfExists(string dbName)
        {
            var connStr = $"Host={Host};Port={Port};Username={Username};Password={Password};Database=postgres";
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            
            // Terminate active connections to the database to prevent locking
            await using var termCmd = new NpgsqlCommand($@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{dbName}'
                  AND pid <> pg_backend_pid();", conn);
            try { await termCmd.ExecuteNonQueryAsync(); } catch { }

            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\"", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            await CleanAllObjects(_srcConfig);
            await CleanAllObjects(_tgtConfig);
        }

        private async Task CleanAllObjects(DatabaseConfig config)
        {
            try
            {
                await using var conn = new NpgsqlConnection(config.GetConnectionString());
                await conn.OpenAsync();
                
                // Drop views, tables, routines, enums, etc.
                await using var cmd = new NpgsqlCommand(@"
                    DROP TRIGGER IF EXISTS trg_test_status ON public.test_idx_trg_table CASCADE;
                    DROP FUNCTION IF EXISTS public.test_trigger_func() CASCADE;
                    DROP FUNCTION IF EXISTS public.test_func(integer) CASCADE;
                    DROP VIEW IF EXISTS public.test_view CASCADE;
                    DROP MATERIALIZED VIEW IF EXISTS public.test_mview CASCADE;
                    DROP TABLE IF EXISTS public.test_fk_child CASCADE;
                    DROP TABLE IF EXISTS public.test_fk_parent CASCADE;
                    DROP TABLE IF EXISTS public.test_composite_pk CASCADE;
                    DROP TABLE IF EXISTS public.test_no_pk CASCADE;
                    DROP TABLE IF EXISTS public.test_added_table CASCADE;
                    DROP TABLE IF EXISTS public.test_deleted_table CASCADE;
                    DROP TABLE IF EXISTS public.test_schema_table CASCADE;
                    DROP TABLE IF EXISTS public.test_data_table CASCADE;
                    DROP TABLE IF EXISTS public.test_idx_trg_table CASCADE;
                    DROP TABLE IF EXISTS public.test_ignore_table CASCADE;
                    DROP TABLE IF EXISTS public.test_filter_table CASCADE;
                    DROP TABLE IF EXISTS public.test_upsert_table CASCADE;
                    DROP SEQUENCE IF EXISTS public.test_seq CASCADE;
                    DROP TYPE IF EXISTS public.test_status_enum CASCADE;
                    DROP TYPE IF EXISTS public.test_comp_type CASCADE;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        [Test]
        public async Task CompareSchema_ShouldDetectTableDifferencesCorrectly()
        {
            // Arrange
            // 1. Setup Source Schema
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE public.test_schema_table (
                        id uuid PRIMARY KEY,
                        name varchar(255) NOT NULL,
                        age integer NULL,
                        data jsonb
                    );
                    ALTER TABLE public.test_schema_table ADD CONSTRAINT age_check CHECK (age >= 18);
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Setup Target Schema (with differences: name varchar(100) instead of 255, age not null, missing data jsonb)
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE public.test_schema_table (
                        id uuid PRIMARY KEY,
                        name varchar(100) NOT NULL,
                        age integer NOT NULL
                    );
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            Assert.That(diffs.Any(d => d.ObjectName == "test_schema_table" && d.DiffType == "Altered"), Is.True, "Table difference should be Altered");
        }

        [Test]
        public async Task CompareSchema_ViewsAndMatViews_ShouldDetectChanges()
        {
            // Arrange
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE VIEW public.test_view AS SELECT 1 AS val;
                    CREATE MATERIALIZED VIEW public.test_mview AS SELECT 10 AS score;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE VIEW public.test_view AS SELECT 2 AS val;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            // View exists in both but definition differs
            Assert.That(diffs.Any(d => d.ObjectName == "test_view" && d.ObjectType == "View" && d.DiffType == "Altered"), Is.True, "View should be detected as Altered");
            // Materialized View only exists in Source
            Assert.That(diffs.Any(d => d.ObjectName == "test_mview" && d.ObjectType == "Materialized View" && d.DiffType == "Added"), Is.True, "MatView should be detected as Added");
        }

        [Test]
        public async Task CompareSchema_Routines_ShouldDetectChanges()
        {
            // Arrange
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE FUNCTION public.test_func(x integer) RETURNS integer AS $$
                    BEGIN
                        RETURN x + 10;
                    END;
                    $$ LANGUAGE plpgsql;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE FUNCTION public.test_func(x integer) RETURNS integer AS $$
                    BEGIN
                        RETURN x + 20;
                    END;
                    $$ LANGUAGE plpgsql;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            Assert.That(diffs.Any(d => d.ObjectName.StartsWith("test_func") && d.ObjectType == "Routine" && d.DiffType == "Altered"), Is.True, "Function should be detected as Altered due to code change");
        }

        [Test]
        public async Task CompareSchema_IndexesAndTriggers_ShouldDetectChanges()
        {
            // Arrange
            var tableDdl = "CREATE TABLE public.test_idx_trg_table (id integer PRIMARY KEY, name varchar(50), status varchar(20));";
            
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(tableDdl + @"
                    CREATE INDEX idx_test_status ON public.test_idx_trg_table(status);
                    CREATE FUNCTION public.test_trigger_func() RETURNS trigger AS $$
                    BEGIN
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql;
                    CREATE TRIGGER trg_test_status BEFORE INSERT ON public.test_idx_trg_table
                    FOR EACH ROW EXECUTE FUNCTION public.test_trigger_func();
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(tableDdl, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            // Index only in Source -> Added
            Assert.That(diffs.Any(d => d.ObjectName == "idx_test_status" && d.ObjectType == "Index" && d.DiffType == "Added"), Is.True, "Index should be detected as Added");
            // Trigger only in Source -> Added
            Assert.That(diffs.Any(d => d.ObjectName == "trg_test_status" && d.ObjectType == "Trigger" && d.DiffType == "Added"), Is.True, "Trigger should be detected as Added");
        }

        [Test]
        public async Task CompareSchema_EnumsAndTypes_ShouldDetectChanges()
        {
            // Arrange
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TYPE public.test_status_enum AS ENUM ('Active', 'Inactive', 'Pending');
                    CREATE TYPE public.test_comp_type AS (field1 varchar(50), field2 integer);
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Target has none
            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            Assert.That(diffs.Any(d => d.ObjectName == "test_status_enum" && d.ObjectType == "Enum" && d.DiffType == "Added"), Is.True, "Enum should be detected as Added");
            Assert.That(diffs.Any(d => d.ObjectName == "test_comp_type" && d.ObjectType == "Type" && d.DiffType == "Added"), Is.True, "Composite Type should be detected as Added");
        }

        [Test]
        public async Task CompareSchema_Sequences_ShouldDetectChanges()
        {
            // Arrange
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE SEQUENCE public.test_seq START WITH 100 INCREMENT BY 5;
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Target has none
            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs, Is.Not.Null);
            Assert.That(diffs.Any(d => d.ObjectName == "test_seq" && d.ObjectType == "Sequence" && d.DiffType == "Added"), Is.True, "Sequence should be detected as Added");
        }

        [Test]
        public async Task CompareAndSyncData_ShouldSyncSuccessfully()
        {
            // Arrange
            var ddl = @"
                CREATE TABLE public.test_data_table (
                    id uuid PRIMARY KEY,
                    name varchar(100) NOT NULL,
                    score numeric(10, 2) NULL,
                    info jsonb NULL
                );";

            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed Data
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid(); // Insert case
            var id3 = Guid.NewGuid(); // Delete case
            var id4 = Guid.NewGuid(); // Update case

            // Source data
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO public.test_data_table (id, name, score, info) VALUES 
                    (@id1, 'Row1 - Same', 95.50, '{""role"": ""admin""}'::jsonb),
                    (@id2, 'Row2 - Insert', 88.00, '{""role"": ""user""}'::jsonb),
                    (@id4, 'Row4 - SourceValue', 70.00, NULL);", conn);
                cmd.Parameters.AddWithValue("id1", id1);
                cmd.Parameters.AddWithValue("id2", id2);
                cmd.Parameters.AddWithValue("id4", id4);
                await cmd.ExecuteNonQueryAsync();
            }

            // Target data
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO public.test_data_table (id, name, score, info) VALUES 
                    (@id1, 'Row1 - Same', 95.50, '{""role"": ""admin""}'::jsonb),
                    (@id3, 'Row3 - Delete', 50.00, NULL),
                    (@id4, 'Row4 - TargetValue', 40.00, NULL);", conn);
                cmd.Parameters.AddWithValue("id1", id1);
                cmd.Parameters.AddWithValue("id3", id3);
                cmd.Parameters.AddWithValue("id4", id4);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);
            var options = new DataCompareOptions();

            // Act 1: Compare Data
            var summary = await service.GetTableDataDiffSummaryAsync("test_data_table", "public", "public", options);

            // Assert 1
            Assert.That(summary.InsertedCount, Is.EqualTo(1), "Should detect 1 insert");
            Assert.That(summary.DeletedCount, Is.EqualTo(1), "Should detect 1 delete");
            Assert.That(summary.UpdatedCount, Is.EqualTo(1), "Should detect 1 update");
            Assert.That(summary.SourceRowCount, Is.EqualTo(3), "Source should have 3 rows");
            Assert.That(summary.TargetRowCount, Is.EqualTo(3), "Target should have 3 rows");

            // Act 2: Generate Sync Script
            var script = await service.GenerateDataDiffAsync(new List<string> { "test_data_table" }, "public", "public", options);
            
            // Execute script on target database using PostgresService transaction wrapper
            var tgtPostgresSvc = new PostgresService(_tgtConfig);
            await tgtPostgresSvc.ExecuteSqlWithTransactionAsync(script);

            // Assert 2: Target should match Source exactly
            long targetCount = 0;
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM public.test_data_table", conn);
                targetCount = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            }
            Assert.That(targetCount, Is.EqualTo(3));
        }

        [Test]
        public async Task CompareData_WithIgnoreColumns_ShouldIgnoreCorrectly()
        {
            // Arrange
            var ddl = @"
                CREATE TABLE public.test_ignore_table (
                    id integer PRIMARY KEY,
                    name varchar(50) NOT NULL,
                    updated_at timestamp NOT NULL
                );";

            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed - different updated_at but same name
            var timeSrc = new DateTime(2026, 6, 23, 10, 0, 0);
            var timeTgt = new DateTime(2026, 6, 23, 9, 0, 0);

            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("INSERT INTO public.test_ignore_table VALUES (1, 'TestItem', @time);", conn);
                cmd.Parameters.AddWithValue("time", timeSrc);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("INSERT INTO public.test_ignore_table VALUES (1, 'TestItem', @time);", conn);
                cmd.Parameters.AddWithValue("time", timeTgt);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);
            
            // Act 1: Compare WITHOUT ignoring -> Should be Different
            var summaryWithoutIgnore = await service.GetTableDataDiffSummaryAsync("test_ignore_table", "public", "public", new DataCompareOptions());
            Assert.That(summaryWithoutIgnore.HasDifferences, Is.True);

            // Act 2: Compare WITH ignoring updated_at -> Should be Synchronized
            var options = new DataCompareOptions { IgnoreColumns = new List<string> { "updated_at" } };
            var summaryWithIgnore = await service.GetTableDataDiffSummaryAsync("test_ignore_table", "public", "public", options);
            Assert.That(summaryWithIgnore.HasDifferences, Is.False, "Row differences should be ignored");
        }

        [Test]
        public async Task CompareData_WithWhereClause_ShouldFilterCorrectly()
        {
            // Arrange
            var ddl = "CREATE TABLE public.test_filter_table (id integer PRIMARY KEY, val integer);";
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_filter_table VALUES (1, 100), (2, 200);", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_filter_table VALUES (1, 100), (2, 999);", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act 1: Without filter -> should be Different (id 2 differs)
            var summaryNoFilter = await service.GetTableDataDiffSummaryAsync("test_filter_table", "public", "public", new DataCompareOptions());
            Assert.That(summaryNoFilter.HasDifferences, Is.True);

            // Act 2: Filter with WHERE id = 1 -> should be Synchronized
            var options = new DataCompareOptions { WhereClause = "id = 1" };
            var summaryFiltered = await service.GetTableDataDiffSummaryAsync("test_filter_table", "public", "public", options);
            Assert.That(summaryFiltered.HasDifferences, Is.False, "Should be synchronized for row id = 1");
        }

        [Test]
        public async Task CompareData_UpsertLogic_ShouldGenerateUpsertScript()
        {
            // Arrange
            var ddl = "CREATE TABLE public.test_upsert_table (id integer PRIMARY KEY, name varchar(50));";
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_upsert_table VALUES (1, 'NewValue');", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_upsert_table VALUES (1, 'OldValue');", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);
            
            // Act
            var options = new DataCompareOptions { UseUpsert = true };
            var script = await service.GenerateDataDiffAsync(new List<string> { "test_upsert_table" }, "public", "public", options);

            // Assert
            Assert.That(script, Does.Contain("ON CONFLICT"), "Upsert script must use ON CONFLICT clause");
        }

        [Test]
        public async Task CompareSchema_IdenticalSchemas_ShouldReturnNoDifferences()
        {
            // Arrange
            var ddl = @"
                CREATE TABLE public.test_schema_table (
                    id uuid PRIMARY KEY,
                    name varchar(255) NOT NULL
                );";
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            var tableDiffs = diffs.Where(d => d.ObjectName == "test_schema_table").ToList();
            Assert.That(tableDiffs, Is.Empty, "Identical tables should have no schema differences");
        }

        [Test]
        public async Task CompareSchema_TableAddedAndDeleted_ShouldDetectCorrectly()
        {
            // Arrange
            // Source has test_added_table, Target does not
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("CREATE TABLE public.test_added_table (id integer PRIMARY KEY);", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            // Target has test_deleted_table, Source does not
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("CREATE TABLE public.test_deleted_table (id integer PRIMARY KEY);", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs.Any(d => d.ObjectName == "test_added_table" && d.ObjectType == "Table" && d.DiffType == "Added"), Is.True, "Should detect added table");
            Assert.That(diffs.Any(d => d.ObjectName == "test_deleted_table" && d.ObjectType == "Table" && d.DiffType == "ExistingInTarget"), Is.True, "Should detect deleted table (existing in target)");
        }

        [Test]
        public async Task CompareSchema_ForeignKeys_ShouldDetectDifferences()
        {
            // Arrange
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE public.test_fk_parent (id integer PRIMARY KEY);
                    CREATE TABLE public.test_fk_child (
                        id integer PRIMARY KEY, 
                        parent_id integer,
                        CONSTRAINT fk_test_parent FOREIGN KEY (parent_id) REFERENCES public.test_fk_parent(id)
                    );
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Target has the tables but is missing the foreign key constraint
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE public.test_fk_parent (id integer PRIMARY KEY);
                    CREATE TABLE public.test_fk_child (
                        id integer PRIMARY KEY, 
                        parent_id integer
                    );
                ", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act
            var diffs = await service.GenerateSchemaDiffResultsAsync("public", "public");

            // Assert
            Assert.That(diffs.Any(d => d.ObjectType == "Constraint" && d.DiffType == "Added" && d.ObjectName.Contains("fk_test_parent")), Is.True, "Should detect added FK constraint");
        }

        [Test]
        public async Task CompareData_CompositePrimaryKey_ShouldSyncCorrectly()
        {
            // Arrange
            var ddl = @"
                CREATE TABLE public.test_composite_pk (
                    key1 integer NOT NULL,
                    key2 varchar(50) NOT NULL,
                    val varchar(100),
                    PRIMARY KEY (key1, key2)
                );";

            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed Source Data
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO public.test_composite_pk VALUES (1, 'A', 'SourceVal1'), (2, 'B', 'SourceVal2');", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed Target Data (1 same, 1 different)
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO public.test_composite_pk VALUES (1, 'A', 'SourceVal1'), (2, 'B', 'TargetValOld');", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act 1: Compare Data
            var summary = await service.GetTableDataDiffSummaryAsync("test_composite_pk", "public", "public");
            
            // Assert 1: Should detect 1 update
            Assert.That(summary.UpdatedCount, Is.EqualTo(1));
            Assert.That(summary.InsertedCount, Is.EqualTo(0));
            Assert.That(summary.DeletedCount, Is.EqualTo(0));

            // Act 2: Sync
            var script = await service.GenerateDataDiffAsync(new List<string> { "test_composite_pk" }, "public", "public");
            var tgtPostgresSvc = new PostgresService(_tgtConfig);
            await tgtPostgresSvc.ExecuteSqlWithTransactionAsync(script);

            // Assert 2: Verify Target data updated
            string val = "";
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT val FROM public.test_composite_pk WHERE key1 = 2 AND key2 = 'B';", conn);
                val = (string)(await cmd.ExecuteScalarAsync() ?? "");
            }
            Assert.That(val, Is.EqualTo("SourceVal2"), "Value should be synchronized to Source's value");
        }

        [Test]
        public async Task CompareData_NoPrimaryKey_ShouldHandleOrSkip()
        {
            // Arrange
            var ddl = "CREATE TABLE public.test_no_pk (val varchar(100));";
            await using (var conn = new NpgsqlConnection(_srcConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_no_pk VALUES ('Row1');", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var conn = new NpgsqlConnection(_tgtConfig.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(ddl + "INSERT INTO public.test_no_pk VALUES ('Row2');", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var service = new DatabaseCompareService(_srcConfig, _tgtConfig);

            // Act 1: Compare Summary
            var summary = await service.GetTableDataDiffSummaryAsync("test_no_pk", "public", "public");

            // Assert 1: Should return empty/zeros since table has no PK
            Assert.That(summary.HasDifferences, Is.False, "Should not detect diffs without PK");
            Assert.That(summary.InsertedCount, Is.EqualTo(0));
            Assert.That(summary.DeletedCount, Is.EqualTo(0));
            Assert.That(summary.UpdatedCount, Is.EqualTo(0));

            // Act 2: Generate Sync Script
            var script = await service.GenerateDataDiffAsync(new List<string> { "test_no_pk" }, "public", "public");

            // Assert 2: Should not generate INSERT/UPDATE/DELETE statement for this table
            Assert.That(script, Does.Not.Contain("INSERT INTO public.test_no_pk"), "Should skip inserts without PK");
            Assert.That(script, Does.Not.Contain("DELETE FROM public.test_no_pk"), "Should skip deletes without PK");
        }
    }
}
