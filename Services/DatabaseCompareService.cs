using Npgsql;
using ReleasePrepTool.Models;
using System.Text;

namespace ReleasePrepTool.Services;

public class DataDiffSummary
{
    public string TableName { get; set; } = "";
    public int InsertedCount { get; set; }
    public int DeletedCount { get; set; }
    public int UpdatedCount { get; set; }
    public bool HasDifferences => InsertedCount > 0 || DeletedCount > 0 || UpdatedCount > 0;
}

public class DataRowDiff
{
    public string DiffType { get; set; } = ""; // "Added", "Removed", "Changed", "Same"
    public Dictionary<string, object> SourceData { get; set; } = new();
    public Dictionary<string, object> TargetData { get; set; } = new();
    public List<string> ChangedColumns { get; set; } = new();
}

public class DataCompareOptions
{
    public List<string> IgnoreColumns { get; set; } = new();
    public string WhereClause { get; set; } = "";
    public bool UseUpsert { get; set; }
    public int BatchSize { get; set; } = 50000;
}

public class DatabaseCompareService
{
    private readonly DatabaseConfig _sourceConfig;
    private readonly DatabaseConfig _targetConfig;

    public DatabaseCompareService(DatabaseConfig sourceConfig, DatabaseConfig targetConfig)
    {
        _sourceConfig = sourceConfig;
        _targetConfig = targetConfig;
    }

    public async Task<List<string>> GetSchemasAsync(DatabaseConfig config)
    {
        var schemas = new List<string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname != 'information_schema' ORDER BY nspname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) schemas.Add(reader.GetString(0));
        return schemas;
    }

    public async Task<List<SchemaDiffResult>> GenerateSchemaDiffResultsAsync(string sourceSchema, string targetSchema)
    {
        var results = new List<SchemaDiffResult>();

        // 1. Get Tables and Columns for both databases
        var targetCols = await GetColumnsAsync(_targetConfig, targetSchema);
        var sourceCols = await GetColumnsAsync(_sourceConfig, sourceSchema);

        var targetTables = targetCols.Select(c => c.TableName).Distinct();
        var sourceTables = sourceCols.Select(c => c.TableName).Distinct();

        // 2. Get Views
        var targetViews = await GetViewsAsync(_targetConfig, targetSchema);
        var sourceViews = await GetViewsAsync(_sourceConfig, sourceSchema);

        // 3. Get Routines (Functions/Procedures)
        var targetRoutines = await GetRoutinesAsync(_targetConfig, targetSchema);
        var sourceRoutines = await GetRoutinesAsync(_sourceConfig, sourceSchema);

        // 4. Get Indexes
        var targetIndexes = await GetIndexesAsync(_targetConfig, targetSchema);
        var sourceIndexes = await GetIndexesAsync(_sourceConfig, sourceSchema);

        // 5. Get Triggers
        var targetTriggers = await GetTriggersAsync(_targetConfig, targetSchema);
        var sourceTriggers = await GetTriggersAsync(_sourceConfig, sourceSchema);

        // 6. Get Constraints
        var targetConstraints = await GetConstraintsAsync(_targetConfig, targetSchema);
        var sourceConstraints = await GetConstraintsAsync(_sourceConfig, sourceSchema);

        // 7. Get Extensions
        var targetExtensions = await GetExtensionsAsync(_targetConfig);
        var sourceExtensions = await GetExtensionsAsync(_sourceConfig);

        // 8. Get Roles
        var targetRoles = await GetRolesAsync(_targetConfig);
        var sourceRoles = await GetRolesAsync(_sourceConfig);

        // 9. Get Sequences
        var targetSequences = await GetSequencesAsync(_targetConfig, targetSchema);
        var sourceSequences = await GetSequencesAsync(_sourceConfig, sourceSchema);

        // 10. Get Enums
        var targetEnums = await GetEnumsAsync(_targetConfig, targetSchema);
        var sourceEnums = await GetEnumsAsync(_sourceConfig, sourceSchema);

        // 11. Get Materialized Views
        var targetMatViews = await GetMaterializedViewsAsync(_targetConfig, targetSchema);
        var sourceMatViews = await GetMaterializedViewsAsync(_sourceConfig, sourceSchema);

        // Added Tables (In Source/New but not in Target/Old)
        var addedTables = sourceTables.Except(targetTables).ToList();
        foreach (var table in addedTables)
        {
            var cols = sourceCols.Where(c => c.TableName == table).ToList();
            var targetDdl = new StringBuilder();
            targetDdl.AppendLine($"CREATE TABLE {targetSchema}.\"{table}\" (");
            var colDefs = cols.Select(c => $"    {c.ColumnName} {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}");
            targetDdl.AppendLine(string.Join(",\n", colDefs));
            targetDdl.AppendLine(");");

            results.Add(new SchemaDiffResult {
                ObjectType = "Table",
                ObjectName = table,
                DiffType = "Added",
                SourceDDL = targetDdl.ToString(), // DDL from Source
                TargetDDL = "-- Table does not exist in Target",
                DiffScript = targetDdl.ToString()
            });
        }

        // Removed Tables (In Target/Old but not in Source/New)
        var removedTables = targetTables.Except(sourceTables).ToList();
        foreach (var table in removedTables)
        {
            var cols = targetCols.Where(c => c.TableName == table).ToList();
            var sourceDdl = new StringBuilder();
            sourceDdl.AppendLine($"CREATE TABLE {targetSchema}.\"{table}\" (");
            var colDefs = cols.Select(c => $"    {c.ColumnName} {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}");
            sourceDdl.AppendLine(string.Join(",\n", colDefs));
            sourceDdl.AppendLine(");");

            results.Add(new SchemaDiffResult {
                ObjectType = "Table",
                ObjectName = table,
                DiffType = "ExistingInTarget",
                SourceDDL = "-- Table removed in Source",
                TargetDDL = sourceDdl.ToString(), // DDL from Target
                DiffScript = $"-- Table \"{table}\" was removed from Source. To remove from Target, run manually: DROP TABLE IF EXISTS {targetSchema}.\"{table}\" CASCADE;"
            });
        }

        // Altered Tables
        var commonTables = targetTables.Intersect(sourceTables).ToList();
        foreach (var table in commonTables)
        {
            var targetTableCols = targetCols.Where(c => c.TableName == table).ToList();
            var sourceTableCols = sourceCols.Where(c => c.TableName == table).ToList();

            var sd = new StringBuilder($"CREATE TABLE {sourceSchema}.\"{table}\" (\n");
            sd.AppendLine(string.Join(",\n", sourceTableCols.Select(c => $"    \"{c.ColumnName}\" {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}")));
            sd.AppendLine(");");

            var td = new StringBuilder($"CREATE TABLE {targetSchema}.\"{table}\" (\n");
            td.AppendLine(string.Join(",\n", targetTableCols.Select(c => $"    \"{c.ColumnName}\" {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}")));
            td.AppendLine(");");

            var diff = new StringBuilder();

            var targetColNames = targetTableCols.Select(c => c.ColumnName).ToList();
            var sourceColNames = sourceTableCols.Select(c => c.ColumnName).ToList();

            var addedColNames = sourceColNames.Except(targetColNames);
            foreach (var colName in addedColNames)
            {
                var col = sourceTableCols.First(c => c.ColumnName == colName);
                diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ADD COLUMN \"{col.ColumnName}\" {col.DataType}{(col.CharacterMaximumLength != null ? $"({col.CharacterMaximumLength})" : "")};");
            }

            var removedColNames = targetColNames.Except(sourceColNames);
            foreach (var colName in removedColNames)
            {
                diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" DROP COLUMN \"{colName}\";");
            }

            var commonColNames = targetColNames.Intersect(sourceColNames);
            foreach (var colName in commonColNames)
            {
                var targetCol = targetTableCols.First(c => c.ColumnName == colName);
                var sourceCol = sourceTableCols.First(c => c.ColumnName == colName);

                if (targetCol.DataType != sourceCol.DataType || targetCol.CharacterMaximumLength != sourceCol.CharacterMaximumLength)
                    diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{colName}\" TYPE {sourceCol.DataType}{(sourceCol.CharacterMaximumLength != null ? $"({sourceCol.CharacterMaximumLength})" : "")};");

                if (targetCol.IsNullable == "YES" && sourceCol.IsNullable == "NO")
                    diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" SET NOT NULL;");
                else if (targetCol.IsNullable == "NO" && sourceCol.IsNullable == "YES")
                    diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" DROP NOT NULL;");

                if (targetCol.ColumnDefault != sourceCol.ColumnDefault)
                {
                    if (string.IsNullOrEmpty(sourceCol.ColumnDefault))
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{colName}\" DROP DEFAULT;");
                    else
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{colName}\" SET DEFAULT {sourceCol.ColumnDefault};");
                }
            }

            if (diff.Length > 0)
            {
                results.Add(new SchemaDiffResult {
                    ObjectType = "Table",
                    ObjectName = table,
                    DiffType = "Altered",
                    SourceDDL = sd.ToString(),
                    TargetDDL = td.ToString(),
                    DiffScript = diff.ToString()
                });
            }
        }

        // --- View Comparison ---
        var addedViews = sourceViews.Keys.Except(targetViews.Keys);
        foreach (var v in addedViews) {
            results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Added", SourceDDL = sourceViews[v], TargetDDL = "-- N/A", DiffScript = sourceViews[v].Replace(sourceSchema + ".", targetSchema + ".") });
        }
        var removedViews = targetViews.Keys.Except(sourceViews.Keys);
        foreach (var v in removedViews) {
            results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = targetViews[v], DiffScript = $"-- View \"{v}\" was removed from Source. To remove from Target, run manually: DROP VIEW IF EXISTS {targetSchema}.{v} CASCADE;" });
        }
        var commonViews = targetViews.Keys.Intersect(sourceViews.Keys);
        foreach (var v in commonViews) {
            string sourceDef = sourceViews[v].Replace(sourceSchema + ".", targetSchema + ".");
            if (targetViews[v] != sourceDef) {
                results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Altered", SourceDDL = sourceViews[v], TargetDDL = targetViews[v], DiffScript = $"CREATE OR REPLACE VIEW {targetSchema}.{v} AS {sourceDef.Substring(sourceDef.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase) + 4)}" });
            }
        }

        // --- Routine Comparison ---
        var addedRoutines = sourceRoutines.Keys.Except(targetRoutines.Keys);
        foreach (var r in addedRoutines) {
            results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Added", SourceDDL = sourceRoutines[r], TargetDDL = "-- N/A", DiffScript = sourceRoutines[r].Replace(sourceSchema + ".", targetSchema + ".") });
        }
        var removedRoutines = targetRoutines.Keys.Except(sourceRoutines.Keys);
        foreach (var r in removedRoutines) {
            results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = targetRoutines[r], DiffScript = $"-- Routine \"{r}\" was removed from Source. To remove from Target, run manually: DROP FUNCTION IF EXISTS {targetSchema}.{r} CASCADE;" });
        }
        var commonRoutines = targetRoutines.Keys.Intersect(sourceRoutines.Keys);
        foreach (var r in commonRoutines) {
            string sourceDef = sourceRoutines[r].Replace(sourceSchema + ".", targetSchema + ".");
            if (targetRoutines[r] != sourceDef) {
                results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Altered", SourceDDL = sourceRoutines[r], TargetDDL = targetRoutines[r], DiffScript = sourceDef });
            }
        }

        // --- Index Comparison ---
        await CompareGenericObjectsAsync(results, "Index", targetIndexes, sourceIndexes, targetSchema);

        // --- Trigger Comparison ---
        await CompareGenericObjectsAsync(results, "Trigger", targetTriggers, sourceTriggers, targetSchema);

        // --- Constraint Comparison ---
        await CompareGenericObjectsAsync(results, "Constraint", targetConstraints, sourceConstraints, targetSchema);

        // --- Extension Comparison ---
        foreach (var ext in sourceExtensions.Keys.Except(targetExtensions.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Extension", ObjectName = ext, DiffType = "Added", SourceDDL = $"CREATE EXTENSION IF NOT EXISTS \"{ext}\" VERSION '{sourceExtensions[ext]}';", TargetDDL = "-- N/A", DiffScript = $"CREATE EXTENSION IF NOT EXISTS \"{ext}\" VERSION '{sourceExtensions[ext]}';" });
        }
        foreach (var ext in targetExtensions.Keys.Except(sourceExtensions.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Extension", ObjectName = ext, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = $"CREATE EXTENSION \"{ext}\" VERSION '{targetExtensions[ext]}';", DiffScript = $"-- Extension \"{ext}\" was removed from Source. To remove from Target, run manually: DROP EXTENSION IF EXISTS \"{ext}\";" });
        }
        foreach (var ext in targetExtensions.Keys.Intersect(sourceExtensions.Keys))
        {
            if (targetExtensions[ext] != sourceExtensions[ext])
            {
                results.Add(new SchemaDiffResult { ObjectType = "Extension", ObjectName = ext, DiffType = "Altered", SourceDDL = $"VERSION '{sourceExtensions[ext]}'", TargetDDL = $"VERSION '{targetExtensions[ext]}'", DiffScript = $"ALTER EXTENSION \"{ext}\" UPDATE TO '{sourceExtensions[ext]}';" });
            }
        }

        // --- Role Comparison ---
        foreach (var role in sourceRoles.Keys.Except(targetRoles.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Role", ObjectName = role, DiffType = "Added", SourceDDL = sourceRoles[role], TargetDDL = "-- N/A", DiffScript = sourceRoles[role] });
        }
        foreach (var role in targetRoles.Keys.Except(sourceRoles.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Role", ObjectName = role, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = targetRoles[role], DiffScript = $"-- Role \"{role}\" was removed from Source. To remove from Target, run manually: DROP ROLE IF EXISTS \"{role}\";" });
        }
        foreach (var role in targetRoles.Keys.Intersect(sourceRoles.Keys))
        {
            if (targetRoles[role] != sourceRoles[role])
            {
                results.Add(new SchemaDiffResult { ObjectType = "Role", ObjectName = role, DiffType = "Altered", SourceDDL = sourceRoles[role], TargetDDL = targetRoles[role], DiffScript = sourceRoles[role].Replace("CREATE ROLE", "ALTER ROLE") });
            }
        }

        // --- Sequence Comparison ---
        await CompareGenericObjectsAsync(results, "Sequence", targetSequences, sourceSequences, targetSchema);

        // --- Enum Comparison ---
        foreach (var name in sourceEnums.Keys.Except(targetEnums.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Enum", ObjectName = name, DiffType = "Added", SourceDDL = sourceEnums[name], TargetDDL = "-- N/A", DiffScript = sourceEnums[name] });
        }
        foreach (var name in targetEnums.Keys.Except(sourceEnums.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Enum", ObjectName = name, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = targetEnums[name], DiffScript = $"-- Enum \"{name}\" was removed from Source. To remove from Target, run manually: DROP TYPE IF EXISTS {targetSchema}.{name};" });
        }
        foreach (var name in targetEnums.Keys.Intersect(sourceEnums.Keys))
        {
            if (targetEnums[name] != sourceEnums[name])
            {
                // Note: Altering enums is restricted in PG (can add values but not remove easily).
                // Simplified approach: recommend manual recreation or adding values.
                results.Add(new SchemaDiffResult { ObjectType = "Enum", ObjectName = name, DiffType = "Altered", SourceDDL = sourceEnums[name], TargetDDL = targetEnums[name], DiffScript = $"-- WARNING: Enum \"{name}\" differs in values. Re-creating or syncing enum values is manual in this tool.\n-- Desired: {sourceEnums[name]}" });
            }
        }

        // --- Materialized View Comparison ---
        foreach (var name in sourceMatViews.Keys.Except(targetMatViews.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "Added", SourceDDL = sourceMatViews[name], TargetDDL = "-- N/A", DiffScript = sourceMatViews[name].Replace(sourceSchema + ".", targetSchema + ".") });
        }
        foreach (var name in targetMatViews.Keys.Except(sourceMatViews.Keys))
        {
            results.Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "ExistingInTarget", SourceDDL = "-- N/A", TargetDDL = targetMatViews[name], DiffScript = $"-- Materialized View \"{name}\" was removed from Source. To remove from Target, run manually: DROP MATERIALIZED VIEW IF EXISTS {targetSchema}.{name} CASCADE;" });
        }
        foreach (var name in targetMatViews.Keys.Intersect(sourceMatViews.Keys))
        {
            string sourceDef = sourceMatViews[name].Replace(sourceSchema + ".", targetSchema + ".");
            if (targetMatViews[name] != sourceDef)
            {
                results.Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "Altered", SourceDDL = sourceMatViews[name], TargetDDL = targetMatViews[name], DiffScript = $"DROP MATERIALIZED VIEW IF EXISTS {targetSchema}.{name} CASCADE;\n{sourceDef}" });
            }
        }

        return results;
    }

    private async Task CompareGenericObjectsAsync(List<SchemaDiffResult> results, string type, Dictionary<string, string> oldObjs, Dictionary<string, string> newObjs, string targetSchema)
    {
        var added = newObjs.Keys.Except(oldObjs.Keys);
        foreach (var name in added) {
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "Added", SourceDDL = "-- N/A", TargetDDL = newObjs[name], DiffScript = newObjs[name] });
        }
        var removed = oldObjs.Keys.Except(newObjs.Keys);
        foreach (var name in removed) {
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "ExistingInTarget", SourceDDL = oldObjs[name], TargetDDL = "-- N/A", DiffScript = $"-- {type} \"{name}\" was removed from Source. To remove from Target, run manually: DROP {type.ToUpper()} IF EXISTS {name};" });
        }
        var common = oldObjs.Keys.Intersect(newObjs.Keys);
        foreach (var name in common) {
            if (oldObjs[name] != newObjs[name]) {
                results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "Altered", SourceDDL = oldObjs[name], TargetDDL = newObjs[name], DiffScript = newObjs[name] });
            }
        }
    }

    public async Task<string> GenerateSchemaDiffAsync(string sourceSchema, string targetSchema)
    {
        var results = await GenerateSchemaDiffResultsAsync(sourceSchema, targetSchema);
        var sb = new StringBuilder();
        sb.AppendLine($"-- Schema update script from {_sourceConfig.DatabaseName} to {_targetConfig.DatabaseName} (Source Schema: {sourceSchema}, Target Schema: {targetSchema})");
        sb.AppendLine($"-- Generated at {DateTime.Now}\n");
        foreach(var r in results) {
            sb.AppendLine($"-- {r.ObjectType}: {r.ObjectName} ({r.DiffType})");
            sb.AppendLine(r.DiffScript);
        }
        return sb.ToString();
    }

    public async Task<string> GenerateDataDiffAsync(List<string> tablesToCompare, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
    {
        options ??= new DataCompareOptions();
        var sb = new StringBuilder();
        sb.AppendLine($"-- Data update script from {_sourceConfig.DatabaseName} to {_targetConfig.DatabaseName} (Source Schema: {sourceSchema}, Target Schema: {targetSchema})");
        sb.AppendLine($"-- Generated at {DateTime.Now}");
        if (!string.IsNullOrEmpty(options.WhereClause)) sb.AppendLine($"-- Filter: {options.WhereClause}");
        if (options.IgnoreColumns.Any()) sb.AppendLine($"-- Ignoring: {string.Join(", ", options.IgnoreColumns)}");
        sb.AppendLine();

        foreach (var table in tablesToCompare)
        {
            var pks = await GetPrimaryKeysAsync(_sourceConfig, table, sourceSchema);
            if (!pks.Any())
            {
                sb.AppendLine($"-- WARNING: Table {table} has no primary key. Data comparison skipped.");
                sb.AppendLine();
                continue;
            }

            var oldData = await GetTableDataAsync(_targetConfig, table, pks, targetSchema, options.WhereClause);
            var newData = await GetTableDataAsync(_sourceConfig, table, pks, sourceSchema, options.WhereClause);

            var oldKeys = oldData.Keys.ToList();
            var newKeys = newData.Keys.ToList();

            // Inserted
            var insertedKeys = newKeys.Except(oldKeys).ToList();
            if (insertedKeys.Any())
            {
                foreach (var key in insertedKeys)
                {
                    var row = newData[key];
                    var colNames = row.Keys.Where(c => !options.IgnoreColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
                    var colVals = colNames.Select(c => FormatSqlValue(row[c])).ToList();

                    if (options.UseUpsert)
                    {
                        var updates = colNames.Where(c => !pks.Contains(c, StringComparer.OrdinalIgnoreCase))
                                              .Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");
                        sb.AppendLine($"INSERT INTO {targetSchema}.\"{table}\" ({string.Join(", ", colNames.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", colVals)}) ON CONFLICT ({string.Join(", ", pks.Select(pk => $"\"{pk}\""))}) DO UPDATE SET {string.Join(", ", updates)};");
                    }
                    else
                    {
                        sb.AppendLine($"INSERT INTO {targetSchema}.\"{table}\" ({string.Join(", ", colNames.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", colVals)});");
                    }
                }
            }

            // Deleted
            var deletedKeys = oldKeys.Except(newKeys).ToList();
            if (deletedKeys.Any())
            {
                foreach (var key in deletedKeys)
                {
                    var conditions = string.Join(" AND ", pks.Select(pk => $"\"{pk}\" = {FormatSqlValue(oldData[key][pk])}"));
                    sb.AppendLine($"DELETE FROM {targetSchema}.\"{table}\" WHERE {conditions};");
                }
            }

            // Updated
            var commonKeys = oldKeys.Intersect(newKeys).ToList();
            foreach (var key in commonKeys)
            {
                var oldRow = oldData[key];
                var newRow = newData[key];
                var changedCols = GetUpdatesForCommonKey(oldRow, newRow, options.IgnoreColumns);

                if (changedCols.Any())
                {
                    if (options.UseUpsert)
                    {
                        var colNames = newRow.Keys.Where(c => !options.IgnoreColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
                        var colVals = colNames.Select(c => FormatSqlValue(newRow[c])).ToList();
                        var updates = colNames.Where(c => !pks.Contains(c, StringComparer.OrdinalIgnoreCase))
                                              .Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");
                        sb.AppendLine($"INSERT INTO {targetSchema}.\"{table}\" ({string.Join(", ", colNames.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", colVals)}) ON CONFLICT ({string.Join(", ", pks.Select(pk => $"\"{pk}\""))}) DO UPDATE SET {string.Join(", ", updates)};");
                    }
                    else
                    {
                        var updates = changedCols.Select(col => $"\"{col}\" = {FormatSqlValue(newRow[col])}");
                        var conditions = string.Join(" AND ", pks.Select(pk => $"\"{pk}\" = {FormatSqlValue(oldRow[pk])}"));
                        sb.AppendLine($"UPDATE {targetSchema}.\"{table}\" SET {string.Join(", ", updates)} WHERE {conditions};");
                    }
                }
            }
            if (insertedKeys.Any() || deletedKeys.Any() || commonKeys.Any(k => GetUpdatesForCommonKey(oldData[k], newData[k], options.IgnoreColumns).Any()))
                 sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<DataDiffSummary> GetTableDataDiffSummaryAsync(string table, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
    {
        options ??= new DataCompareOptions();
        var summary = new DataDiffSummary { TableName = table };
        var pks = await GetPrimaryKeysAsync(_sourceConfig, table, sourceSchema);
        if (!pks.Any()) return summary;

        var oldData = await GetTableDataAsync(_targetConfig, table, pks, targetSchema, options.WhereClause);
        var newData = await GetTableDataAsync(_sourceConfig, table, pks, sourceSchema, options.WhereClause);

        var oldKeys = oldData.Keys.ToList();
        var newKeys = newData.Keys.ToList();

        summary.InsertedCount = newKeys.Except(oldKeys).Count();
        summary.DeletedCount = oldKeys.Except(newKeys).Count();

        var commonKeys = oldKeys.Intersect(newKeys).ToList();
        foreach (var key in commonKeys)
        {
            if (GetUpdatesForCommonKey(oldData[key], newData[key], options.IgnoreColumns).Any())
                summary.UpdatedCount++;
        }

        return summary;
    }

    public async Task<List<DataRowDiff>> GetDetailedTableDataDiffAsync(string table, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
    {
        options ??= new DataCompareOptions();
        var diffs = new List<DataRowDiff>();
        var pks = await GetPrimaryKeysAsync(_sourceConfig, table, sourceSchema);
        if (!pks.Any()) return diffs;

        var oldData = await GetTableDataAsync(_targetConfig, table, pks, targetSchema, options.WhereClause);
        var newData = await GetTableDataAsync(_sourceConfig, table, pks, sourceSchema, options.WhereClause);

        var oldKeys = oldData.Keys.OrderBy(k => k).ToList();
        var newKeys = newData.Keys.OrderBy(k => k).ToList();

        // Inserted
        foreach (var key in newKeys.Except(oldKeys).Take(1000))
        {
            diffs.Add(new DataRowDiff { DiffType = "Added", TargetData = newData[key] });
        }

        // Deleted
        foreach (var key in oldKeys.Except(newKeys).Take(1000))
        {
            diffs.Add(new DataRowDiff { DiffType = "Removed", SourceData = oldData[key] });
        }

        // Changed
        foreach (var key in oldKeys.Intersect(newKeys).Take(1000))
        {
            var oldRow = oldData[key];
            var newRow = newData[key];
            var changedCols = GetUpdatesForCommonKey(oldRow, newRow, options.IgnoreColumns);
            
            if (changedCols.Any())
            {
                diffs.Add(new DataRowDiff { 
                    DiffType = "Changed", 
                    SourceData = oldRow, 
                    TargetData = newRow, 
                    ChangedColumns = changedCols 
                });
            }
            else
            {
                diffs.Add(new DataRowDiff { DiffType = "Same", SourceData = oldRow, TargetData = newRow });
            }
        }

        return diffs;
    }

    private List<string> GetUpdatesForCommonKey(Dictionary<string, object> sourceRow, Dictionary<string, object> targetRow, List<string> ignoreColumns)
    {
        var updates = new List<string>();
        foreach (var col in sourceRow.Keys)
        {
            if (ignoreColumns.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;
            if (!targetRow.ContainsKey(col)) continue;

            var v1 = targetRow[col];
            var v2 = sourceRow[col];

            if (v1 == null && v2 == null) continue;
            if (v1 == null || v2 == null || v1 is DBNull || v2 is DBNull)
            {
                if (!(v1 is DBNull && v2 is DBNull) && v1 != v2)
                    updates.Add(col);
                continue;
            }

            // Primitive equality check
            if (Equals(v1, v2)) continue;

            // Specialized checks
            if (v1 is byte[] b1 && v2 is byte[] b2)
            {
                if (!b1.SequenceEqual(b2)) updates.Add(col);
            }
            else if (v1 is DateTime dt1 && v2 is DateTime dt2)
            {
                // Compare with millisecond precision
                if (Math.Abs((dt1 - dt2).TotalMilliseconds) > 1) updates.Add(col);
            }
            else
            {
                // Fallback to string comparison for things like Guid, etc.
                if (Convert.ToString(v1) != Convert.ToString(v2))
                    updates.Add(col);
            }
        }
        return updates;
    }

    private string FormatSqlValue(object value)
    {
        if (value == null || value is DBNull) return "NULL";
        if (value is string s) return $"'{s.Replace("'", "''")}'";
        if (value is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
        if (value is bool b) return b ? "TRUE" : "FALSE";
        if (value is System.Collections.IEnumerable && !(value is string))
        {
            // Handle arrays (simplified)
            return $"'{{{string.Join(",", ((System.Collections.IEnumerable)value).Cast<object>().Select(v => v.ToString() ?? ""))}}}'";
        }
        // Handle potential JSONB (if string looks like JSON)
        var str = value.ToString() ?? "";
        if ((str.StartsWith("{") && str.EndsWith("}")) || (str.StartsWith("[") && str.EndsWith("]")))
            return $"'{str.Replace("'", "''")}'::jsonb";
            
        return str;
    }

    private async Task<List<ColumnInfo>> GetColumnsAsync(DatabaseConfig config, string schemaName)
    {
        var cols = new List<ColumnInfo>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT c.table_name, c.column_name, c.data_type, c.character_maximum_length, c.is_nullable, c.column_default
            FROM information_schema.columns c
            JOIN information_schema.tables t ON c.table_name = t.table_name AND c.table_schema = t.table_schema
            WHERE c.table_schema = $1 AND t.table_type = 'BASE TABLE'
            ORDER BY c.table_name, c.ordinal_position;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cols.Add(new ColumnInfo
            {
                TableName = reader.GetString(0),
                ColumnName = reader.GetString(1),
                DataType = reader.GetString(2),
                CharacterMaximumLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                IsNullable = reader.GetString(4),
                ColumnDefault = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }
        return cols;
    }

    private async Task<List<string>> GetPrimaryKeysAsync(DatabaseConfig config, string tableName, string schemaName)
    {
        var pks = new List<string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
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
        catch {
             // Invalid table or other error
        }

        return pks;
    }

    private async Task<Dictionary<string, Dictionary<string, object>>> GetTableDataAsync(DatabaseConfig config, string tableName, List<string> primaryKeys, string schemaName, string whereClause = "")
    {
        var data = new Dictionary<string, Dictionary<string, object>>();
        
        try 
        {
            await using var conn = new NpgsqlConnection(config.GetConnectionString());
            await conn.OpenAsync();

            var sql = $"SELECT * FROM {schemaName}.\"{tableName}\"";
            if (!string.IsNullOrEmpty(whereClause))
                sql += $" WHERE {whereClause}";
            
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            
            var keyCounts = new Dictionary<string, int>();
            while (await reader.ReadAsync())
            {
                var rowData = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    rowData[reader.GetName(i)] = reader.GetValue(i);
                }

                // Create a base key string based on primary key values
                var pkValues = primaryKeys.Select(pk => rowData.ContainsKey(pk) && rowData[pk] != null && rowData[pk] != DBNull.Value ? Convert.ToString(rowData[pk]) : "NULL");
                var baseKey = string.Join("|", pkValues);
                
                // Track duplicate keys (common with NULL PKs) to ensure all rows are preserved
                if (!keyCounts.ContainsKey(baseKey)) keyCounts[baseKey] = 0;
                keyCounts[baseKey]++;
                var finalKey = keyCounts[baseKey] > 1 ? $"{baseKey}_row{keyCounts[baseKey]}" : baseKey;

                data[finalKey] = rowData;
            }
        }
        catch 
        {
             // Table might not exist in this version
        }

        return data;
    }

    private async Task<Dictionary<string, string>> GetViewsAsync(DatabaseConfig config, string schemaName)
    {
        var views = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT table_name, view_definition FROM information_schema.views WHERE table_schema = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var definition = reader.GetString(1);
            views[name] = $"CREATE VIEW {schemaName}.{name} AS {definition}";
        }
        return views;
    }

    private async Task<Dictionary<string, string>> GetRoutinesAsync(DatabaseConfig config, string schemaName)
    {
        var routines = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        // Get function DDL directly using pg_get_functiondef
        var sql = @"
            SELECT p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')', 
                   pg_get_functiondef(p.oid)
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            routines[reader.GetString(0)] = reader.GetString(1);
        }
        return routines;
    }

    private async Task<Dictionary<string, string>> GetIndexesAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) dict[reader.GetString(0)] = reader.GetString(1);
        return dict;
    }

    private async Task<Dictionary<string, string>> GetTriggersAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT t.tgname, pg_get_triggerdef(t.oid) 
            FROM pg_trigger t
            JOIN pg_class c ON t.tgrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = $1 AND NOT t.tgisinternal;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) dict[reader.GetString(0)] = reader.GetString(1) + ";";
        return dict;
    }

    private async Task<Dictionary<string, string>> GetConstraintsAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT c.conname, pg_get_constraintdef(c.oid), r.relname
            FROM pg_constraint c
            JOIN pg_namespace n ON c.connamespace = n.oid
            JOIN pg_class r ON c.conrelid = r.oid
            WHERE n.nspname = $1 AND c.contype IN ('f', 'u', 'c');";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var conname = reader.GetString(0);
            var def = reader.GetString(1);
            var relname = reader.GetString(2);
            dict[conname] = $"ALTER TABLE {schemaName}.{relname} ADD CONSTRAINT {conname} {def};";
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetExtensionsAsync(DatabaseConfig config)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT extname, extversion FROM pg_extension;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetRolesAsync(DatabaseConfig config)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT rolname, rolsuper, rolinherit, rolcreaterole, rolcreatedb, rolcanlogin 
            FROM pg_roles 
            WHERE rolname NOT LIKE 'pg_%' AND rolname NOT LIKE 'azuresu%';"; // Exclude system roles
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var super = reader.GetBoolean(1) ? "SUPERUSER" : "NOSUPERUSER";
            var inherit = reader.GetBoolean(2) ? "INHERIT" : "NOINHERIT";
            var createRole = reader.GetBoolean(3) ? "CREATEROLE" : "NOCREATEROLE";
            var createDb = reader.GetBoolean(4) ? "CREATEDB" : "NOCREATEDB";
            var login = reader.GetBoolean(5) ? "LOGIN" : "NOLOGIN";
            
            dict[name] = $"CREATE ROLE \"{name}\" WITH {login} {super} {inherit} {createRole} {createDb};";
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetSequencesAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT 
                c.relname AS sequence_name,
                s.seqstart, s.seqincrement, s.seqmin, s.seqmax, s.seqcache, s.seqcycle
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_sequence s ON s.seqrelid = c.oid
            WHERE c.relkind = 'S' AND n.nspname = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var start = reader.GetInt64(1);
            var inc = reader.GetInt64(2);
            var min = reader.GetInt64(3);
            var max = reader.GetInt64(4);
            var cache = reader.GetInt64(5);
            var cycle = reader.GetBoolean(6) ? "CYCLE" : "NO CYCLE";
            
            dict[name] = $"CREATE SEQUENCE {schemaName}.\"{name}\" START WITH {start} INCREMENT BY {inc} MINVALUE {min} MAXVALUE {max} CACHE {cache} {cycle};";
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetEnumsAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT
                t.typname AS enum_name,
                string_agg('''' || e.enumlabel || '''', ', ' ORDER BY e.enumsortorder) AS enum_vals
            FROM pg_type t
            JOIN pg_enum e ON t.oid = e.enumtypid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = $1
            GROUP BY t.typname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var vals = reader.GetString(1);
            dict[name] = $"CREATE TYPE {schemaName}.\"{name}\" AS ENUM ({vals});";
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetMaterializedViewsAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT matviewname, definition FROM pg_matviews WHERE schemaname = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var def = reader.GetString(1);
            dict[name] = $"CREATE MATERIALIZED VIEW {schemaName}.{name} AS {def}";
        }
        return dict;
    }

    private class ColumnInfo
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public int? CharacterMaximumLength { get; set; }
        public string IsNullable { get; set; } = "";
        public string ColumnDefault { get; set; } = "";
    }
}
