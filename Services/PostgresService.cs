using Npgsql;
using ReleasePrepTool.Models;
using System.Diagnostics;

namespace ReleasePrepTool.Services;

public class PostgresService
{
    private readonly DatabaseConfig _config;
    public string PostgresBinPath { get; set; } = "";

    public class JunkRecord
    {
        public string? TableName { get; set; }
        public string? PrimaryKeyColumn { get; set; }
        public string? PrimaryKeyValue { get; set; }
        public string? ColumnName { get; set; }
        public string? DetectedContent { get; set; }
    }

    public PostgresService(DatabaseConfig config)
    {
        _config = config;
    }

    public async Task<List<string>> GetAllDatabasesAsync()
    {
        var tempConfig = new DatabaseConfig
        {
            Host = _config.Host,
            Port = _config.Port,
            Username = _config.Username,
            Password = _config.Password,
            DatabaseName = "postgres" // default db to connect to
        };

        var databases = new List<string>();
        await using var conn = new NpgsqlConnection(tempConfig.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datistemplate = false;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    public async Task<List<string>> GetPrimaryKeysAsync(string dbName, string tableName, string schemaName = "public")
    {
        var pks = new List<string>();
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT a.attname
            FROM   pg_index i
            JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE  i.indrelid = CAST(quote_ident($1) || '.' || quote_ident($2) AS regclass) AND i.indisprimary;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(tableName);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pks.Add(reader.GetString(0));
            }
        }
        catch { }

        return pks;
    }

    public async Task<List<string>> GetSchemaTablesAsync(string dbName, string schemaName)
    {
        var tables = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relkind IN ('r', 'p') ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public async Task<List<string>> GetSchemasAsync(string dbName)
    {
        var schemas = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        
        var sql = "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ('information_schema', 'pg_catalog') AND schema_name NOT LIKE 'pg_toast%' AND schema_name NOT LIKE 'pg_temp%';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schemas.Add(reader.GetString(0));
        }
        return schemas;
    }

    public async Task<List<string>> GetSchemaViewsAsync(string dbName, string schemaName)
    {
        var views = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT table_name FROM information_schema.views WHERE table_schema = $1 ORDER BY table_name;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            views.Add(reader.GetString(0));
        }
        return views;
    }

    public async Task<List<string>> GetSchemaRoutinesAsync(string dbName, string schemaName)
    {
        var routines = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT routine_name FROM information_schema.routines WHERE routine_schema = $1 ORDER BY routine_name;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            routines.Add(reader.GetString(0));
        }
        return routines;
    }

    public async Task<List<(string Table, string Column)>> GetSchemaColumnsAsync(string dbName, string schemaName)
    {
        var cols = new List<(string, string)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = $1 ORDER BY table_name, column_name;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) cols.Add((reader.GetString(0), reader.GetString(1)));
        return cols;
    }

    public async Task<List<string>> GetSchemaIndexesAsync(string dbName, string schemaName)
    {
        var indexes = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('i', 'I') AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        return indexes;
    }

    public async Task<List<string>> GetSchemaTypesAsync(string dbName, string schemaName)
    {
        var types = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT typname FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = $1 AND t.typtype IN ('b', 'c', 'e', 'm', 'r') AND t.typname NOT LIKE '\\_%' ORDER BY typname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) types.Add(reader.GetString(0));
        return types;
    }

    public async Task<List<string>> GetRolesAsync(string dbName)
    {
        var roles = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT rolname FROM pg_roles WHERE rolname NOT LIKE 'pg_%' AND rolname NOT LIKE 'rds%' ORDER BY rolname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
        return roles;
    }

    public async Task<List<(string Table, string Trigger)>> GetSchemaTriggersAsync(string dbName, string schemaName)
    {
        var triggers = new List<(string, string)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT c.relname, t.tgname FROM pg_trigger t 
                    JOIN pg_class c ON c.oid = t.tgrelid 
                    JOIN pg_namespace n ON n.oid = c.relnamespace 
                    WHERE n.nspname = $1 AND t.tgisinternal = false 
                    ORDER BY t.tgname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) triggers.Add((reader.GetString(0), reader.GetString(1)));
        return triggers;
    }

    public async Task<List<(string Table, string Constraint)>> GetSchemaConstraintsAsync(string dbName, string schemaName)
    {
        var constraints = new List<(string, string)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT 
                        COALESCE(c.relname, 'Domain: ' || t.typname) as parent, 
                        con.conname 
                    FROM pg_constraint con 
                    JOIN pg_namespace n ON n.oid = con.connamespace 
                    LEFT JOIN pg_class c ON c.oid = con.conrelid 
                    LEFT JOIN pg_type t ON t.oid = con.contypid 
                    WHERE n.nspname = $1 
                    ORDER BY con.conname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) constraints.Add((reader.GetString(0), reader.GetString(1)));
        return constraints;
    }

    public async Task<List<string>> GetSchemaPartitionsAsync(string dbName, string schemaName)
    {
        var parts = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT c.relname FROM pg_class c 
                    JOIN pg_namespace n ON n.oid = c.relnamespace 
                    WHERE n.nspname = $1 AND (c.relkind = 'p' OR c.relispartition = true) 
                    ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) parts.Add(reader.GetString(0));
        return parts;
    }

    public async Task<List<string>> GetSchemaMatViewsAsync(string dbName, string schemaName)
    {
        var views = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'm' AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) views.Add(reader.GetString(0));
        return views;
    }

    public async Task<List<string>> GetSchemaSequencesAsync(string dbName, string schemaName)
    {
        var seqs = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'S' AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) seqs.Add(reader.GetString(0));
        return seqs;
    }

    public async Task<List<string>> GetSchemaDomainsAsync(string dbName, string schemaName)
    {
        var domains = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT typname FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = $1 AND t.typtype = 'd' ORDER BY typname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) domains.Add(reader.GetString(0));
        return domains;
    }

    public async Task<string> GetObjectCommentAsync(string dbName, string schemaName, string objectName, JunkType type)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        string sql = "";
        
        switch (type) {
            case JunkType.Table:
            case JunkType.View:
            case JunkType.MaterializedView:
            case JunkType.Sequence:
            case JunkType.Index:
            case JunkType.Partition:
                sql = "SELECT obj_description(c.oid, 'pg_class') FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Routine:
                sql = "SELECT obj_description(p.oid, 'pg_proc') FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proname = $2 LIMIT 1;";
                break;
            case JunkType.Trigger:
                sql = "SELECT obj_description(t.oid, 'pg_trigger') FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND t.tgname = $2 LIMIT 1;";
                break;
            case JunkType.Constraint:
                sql = "SELECT obj_description(con.oid, 'pg_constraint') FROM pg_constraint con JOIN pg_namespace n ON n.oid = con.connamespace WHERE n.nspname = $1 AND con.conname = $2 LIMIT 1;";
                break;
            case JunkType.Role:
                sql = "SELECT obj_description(r.oid, 'pg_authid') FROM pg_roles r WHERE r.rolname = $1 LIMIT 1;";
                return await GetGlobalCommentAsync(dbName, objectName, "pg_authid");
            default:
                return "";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(objectName);
        var res = await cmd.ExecuteScalarAsync();
        return res?.ToString() ?? "";
    }

    private async Task<string> GetGlobalCommentAsync(string dbName, string objectName, string typeName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"SELECT obj_description(oid, '{typeName}') FROM pg_roles WHERE rolname = $1 LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(objectName);
        var res = await cmd.ExecuteScalarAsync();
        return res?.ToString() ?? "";
    }

    public async Task<string> GetObjectDefinitionAsync(string dbName, string schemaName, string objectName, JunkType type)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        string sql = "";
        
        switch (type) {
            case JunkType.Routine: 
                sql = "SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proname = $2 LIMIT 1;"; 
                break;
            case JunkType.View: 
            case JunkType.MaterializedView:
                sql = "SELECT 'CREATE OR REPLACE ' || (CASE WHEN c.relkind = 'm' THEN 'MATERIALIZED VIEW ' ELSE 'VIEW ' END) || n.nspname || '.' || c.relname || ' AS\n' || pg_get_viewdef(c.oid, true) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Trigger:
                sql = "SELECT pg_get_triggerdef(t.oid, true) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND t.tgname = $2 LIMIT 1;";
                break;
            case JunkType.Index:
                sql = "SELECT pg_get_indexdef(c.oid) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Constraint:
                sql = "SELECT pg_get_constraintdef(con.oid, true) FROM pg_constraint con JOIN pg_namespace n ON n.oid = con.connamespace WHERE n.nspname = $1 AND con.conname = $2 LIMIT 1;";
                break;
            case JunkType.Table:
            case JunkType.Partition:
                // Simplified DDL for Table/Partition name only (getting full DDL is complex without pg_dump)
                return $"-- Table/Partition: {schemaName}.{objectName}\n-- (Deep Scan check name only for now)";
            default:
                return $"-- Definition for {type}: {schemaName}.{objectName}";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(objectName);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? $"-- No definition found for {type} {schemaName}.{objectName}";
    }

    public async Task<List<string>> GetSchemaAggregatesAsync(string dbName, string schemaName)
    {
        var aggs = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proisagg = true ORDER BY proname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        try {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) aggs.Add(reader.GetString(0));
        } catch { /* proisagg was changed to prokind in recent postgres version */
            await using var cmd2 = new NpgsqlCommand("SELECT proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.prokind = 'a' ORDER BY proname;", conn);
            cmd2.Parameters.AddWithValue(schemaName);
            await using var reader2 = await cmd2.ExecuteReaderAsync();
            while (await reader2.ReadAsync()) aggs.Add(reader2.GetString(0));
        }
        return aggs;
    }

    public async Task<long> GetTableRowCountAsync(string tableName, string schemaName = "public")
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {schemaName}.\"{tableName}\"", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public async Task BackupDatabaseAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F c -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public async Task DumpFullScriptAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F p -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public async Task CreateDatabaseIfNotExistsAsync(string dbName)
    {
        // Connect to the default 'postgres' db to issue CREATE DATABASE
        var adminConnStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database=postgres";
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @name", conn);
        checkCmd.Parameters.AddWithValue("name", dbName);
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists == null)
        {
            // CREATE DATABASE cannot run in a transaction
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task RestoreDatabaseAsync(string fileType, string backupPath, string? targetDbName = null, Action<string>? onOutput = null)
    {
        var db = string.IsNullOrWhiteSpace(targetDbName) ? _config.DatabaseName : targetDbName;
        if (!string.IsNullOrWhiteSpace(targetDbName))
        {
            onOutput?.Invoke($"Checking / creating database '{targetDbName}'...");
            await CreateDatabaseIfNotExistsAsync(targetDbName);
            onOutput?.Invoke($"Database '{targetDbName}' ready.");
        }

        if (fileType == ".sql")
        {
            await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} -f \"{backupPath}\"", onOutput);
        }
        else
        {
            await RunPostgresToolAsync("pg_restore", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} --clean --if-exists -1 --verbose \"{backupPath}\"", onOutput);
        }
    }

    public async Task ExecuteScriptFileAsync(string scriptPath)
    {
        // using psql for script file execution instead of npgsql to properly handle DO blocks and large scripts
        await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {_config.DatabaseName} -f \"{scriptPath}\" -v ON_ERROR_STOP=1 --single-transaction");
    }

    public async Task ExecuteSqlWithTransactionAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn, transaction);
            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteRecordAsync(string tableName, string pkColumn, string pkValue, string schemaName = "public")
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        var sql = $"DELETE FROM {schemaName}.\"{tableName}\" WHERE \"{pkColumn}\" = @pkValue";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pkValue", pkValue);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DropSchemaAsync(string dbName, string schemaName, bool cascade = true)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP SCHEMA IF EXISTS \"{schemaName}\" {(cascade ? "CASCADE" : "RESTRICT")}";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DropTableAsync(string dbName, string schemaName, string tableName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP TABLE IF EXISTS \"{schemaName}\".\"{tableName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DropViewAsync(string dbName, string schemaName, string viewName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP VIEW IF EXISTS \"{schemaName}\".\"{viewName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DropRoutineAsync(string dbName, string schemaName, string routineName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        // Postgres DROP ROUTINE handles both functions and procedures
        var sql = $"DROP ROUTINE IF EXISTS \"{schemaName}\".\"{routineName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<JunkRecord>> SearchJunkDataAsync(string searchKeyword, string schemaName = "public")
    {
        var results = new List<JunkRecord>();
        var keyword = $"%{searchKeyword}%";

        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();

        // Get all tables with string or text columns
        var metaSql = @"
            SELECT t.table_name, c.column_name
            FROM information_schema.tables t
            JOIN information_schema.columns c ON t.table_name = c.table_name AND t.table_schema = c.table_schema
            WHERE t.table_schema = @schema AND c.data_type IN ('text', 'character varying', 'character')
            AND t.table_type = 'BASE TABLE';";

        var tableCols = new Dictionary<string, List<string>>();
        await using (var cmd = new NpgsqlCommand(metaSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schemaName);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    var col = reader.GetString(1);
                    if (!tableCols.ContainsKey(table)) tableCols[table] = new List<string>();
                    tableCols[table].Add(col);
                }
            }
        }

        foreach (var tc in tableCols)
        {
            var table = tc.Key;
            
            // Try to find PK
            var pkSql = @"
                SELECT a.attname
                FROM   pg_index i
                JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                WHERE  i.indrelid = CAST(quote_ident($1) || '.' || quote_ident($2) AS regclass) AND i.indisprimary LIMIT 1;";
            
            string pkColumn = null;
            await using (var pkCmd = new NpgsqlCommand(pkSql, conn))
            {
                pkCmd.Parameters.AddWithValue(schemaName);
                pkCmd.Parameters.AddWithValue(table);
                try { pkColumn = (string)await pkCmd.ExecuteScalarAsync(); } catch { }
            }

            if (pkColumn == null) continue;

            foreach (var col in tc.Value)
            {
                var searchSql = $"SELECT \"{pkColumn}\", \"{col}\" FROM {schemaName}.\"{table}\" WHERE \"{col}\" ILIKE @kw LIMIT 50";
                await using var searchCmd = new NpgsqlCommand(searchSql, conn);
                searchCmd.Parameters.AddWithValue("kw", keyword);
                
                try 
                {
                    await using var dataReader = await searchCmd.ExecuteReaderAsync();
                    while (await dataReader.ReadAsync())
                    {
                        results.Add(new JunkRecord
                        {
                            TableName = table,
                            PrimaryKeyColumn = pkColumn,
                            PrimaryKeyValue = dataReader[0]?.ToString(),
                            ColumnName = col,
                            DetectedContent = dataReader[1]?.ToString()
                        });
                    }
                } 
                catch { } // handle column cast errors etc gracefully
            }
        }
        
        return results;
    }

    public async Task<Dictionary<string, string>> GetFullRowDataAsync(string dbName, string schemaName, string tableName, string pkColumn, string pkValue)
    {
        var row = new Dictionary<string, string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // Get all columns for the table
        var sql = $"SELECT * FROM {schemaName}.\"{tableName}\" WHERE \"{pkColumn}\" = @pk LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pk", pkValue);

        try {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader[i]?.ToString() ?? "NULL";
                }
            }
        } catch { }

        return row;
    }

    private async Task RunPostgresToolAsync(string toolName, string arguments, Action<string>? onOutput = null)
    {
        string exe = string.IsNullOrEmpty(PostgresBinPath) ? toolName : Path.Combine(PostgresBinPath, toolName);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && string.IsNullOrEmpty(PostgresBinPath))
            exe += ".exe";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.EnvironmentVariables["PGPASSWORD"] = _config.Password;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var errorSb = new System.Text.StringBuilder();

        if (onOutput != null)
        {
            // Stream output lines live
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) { onOutput(e.Data); errorSb.AppendLine(e.Data); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        else
        {
            process.Start();
        }

        string finalStdout = onOutput != null ? "" : await process.StandardOutput.ReadToEndAsync();
        string finalStderr = onOutput != null ? "" : await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = onOutput != null ? errorSb.ToString() : finalStderr;
            throw new Exception($"Error running {toolName}: {err}\n{finalStdout}");
        }
    }
}
