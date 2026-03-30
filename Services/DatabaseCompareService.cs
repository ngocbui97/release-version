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

public class DatabaseCompareService
{
    private readonly DatabaseConfig _oldDbConfig;
    private readonly DatabaseConfig _newDbConfig;

    public DatabaseCompareService(DatabaseConfig oldDbConfig, DatabaseConfig newDbConfig)
    {
        _oldDbConfig = oldDbConfig;
        _newDbConfig = newDbConfig;
    }

    public async Task<List<SchemaDiffResult>> GenerateSchemaDiffResultsAsync()
    {
        var results = new List<SchemaDiffResult>();

        // 1. Get Tables and Columns for both databases
        var oldCols = await GetColumnsAsync(_oldDbConfig);
        var newCols = await GetColumnsAsync(_newDbConfig);

        var oldTables = oldCols.Select(c => c.TableName).Distinct();
        var newTables = newCols.Select(c => c.TableName).Distinct();

        // 2. Get Views
        var oldViews = await GetViewsAsync(_oldDbConfig);
        var newViews = await GetViewsAsync(_newDbConfig);

        // 3. Get Routines (Functions/Procedures)
        var oldRoutines = await GetRoutinesAsync(_oldDbConfig);
        var newRoutines = await GetRoutinesAsync(_newDbConfig);

        // 4. Get Indexes
        var oldIndexes = await GetIndexesAsync(_oldDbConfig);
        var newIndexes = await GetIndexesAsync(_newDbConfig);

        // 5. Get Triggers
        var oldTriggers = await GetTriggersAsync(_oldDbConfig);
        var newTriggers = await GetTriggersAsync(_newDbConfig);

        // 6. Get Constraints
        var oldConstraints = await GetConstraintsAsync(_oldDbConfig);
        var newConstraints = await GetConstraintsAsync(_newDbConfig);

        // Added Tables
        var addedTables = newTables.Except(oldTables).ToList();
        foreach (var table in addedTables)
        {
            var cols = newCols.Where(c => c.TableName == table).ToList();
            var targetDdl = new StringBuilder();
            targetDdl.AppendLine($"CREATE TABLE public.\"{table}\" (");
            var colDefs = cols.Select(c => $"    {c.ColumnName} {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}");
            targetDdl.AppendLine(string.Join(",\n", colDefs));
            targetDdl.AppendLine(");");

            results.Add(new SchemaDiffResult {
                ObjectType = "Table",
                ObjectName = table,
                DiffType = "Added",
                SourceDDL = "-- Table does not exist in Source",
                TargetDDL = targetDdl.ToString(),
                DiffScript = targetDdl.ToString()
            });
        }

        // Removed Tables
        var removedTables = oldTables.Except(newTables).ToList();
        foreach (var table in removedTables)
        {
            var cols = oldCols.Where(c => c.TableName == table).ToList();
            var sourceDdl = new StringBuilder();
            sourceDdl.AppendLine($"CREATE TABLE public.\"{table}\" (");
            var colDefs = cols.Select(c => $"    {c.ColumnName} {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}");
            sourceDdl.AppendLine(string.Join(",\n", colDefs));
            sourceDdl.AppendLine(");");

            results.Add(new SchemaDiffResult {
                ObjectType = "Table",
                ObjectName = table,
                DiffType = "Removed",
                SourceDDL = sourceDdl.ToString(),
                TargetDDL = "-- Table removed in Target",
                DiffScript = $"DROP TABLE IF EXISTS public.\"{table}\" CASCADE;"
            });
        }

        // Altered Tables
        var commonTables = oldTables.Intersect(newTables).ToList();
        foreach (var table in commonTables)
        {
            var oldTableCols = oldCols.Where(c => c.TableName == table).ToList();
            var newTableCols = newCols.Where(c => c.TableName == table).ToList();

            var sd = new StringBuilder($"CREATE TABLE public.\"{table}\" (\n");
            sd.AppendLine(string.Join(",\n", oldTableCols.Select(c => $"    \"{c.ColumnName}\" {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}")));
            sd.AppendLine(");");

            var td = new StringBuilder($"CREATE TABLE public.\"{table}\" (\n");
            td.AppendLine(string.Join(",\n", newTableCols.Select(c => $"    \"{c.ColumnName}\" {c.DataType}{(c.CharacterMaximumLength != null ? $"({c.CharacterMaximumLength})" : "")}{(c.IsNullable == "NO" ? " NOT NULL" : "")}{(string.IsNullOrEmpty(c.ColumnDefault) ? "" : $" DEFAULT {c.ColumnDefault}")}")));
            td.AppendLine(");");

            var diff = new StringBuilder();

            var oldColNames = oldTableCols.Select(c => c.ColumnName).ToList();
            var newColNames = newTableCols.Select(c => c.ColumnName).ToList();

            var addedColNames = newColNames.Except(oldColNames);
            foreach (var colName in addedColNames)
            {
                var col = newTableCols.First(c => c.ColumnName == colName);
                diff.AppendLine($"ALTER TABLE public.\"{table}\" ADD COLUMN \"{col.ColumnName}\" {col.DataType}{(col.CharacterMaximumLength != null ? $"({col.CharacterMaximumLength})" : "")};");
            }

            var removedColNames = oldColNames.Except(newColNames);
            foreach (var colName in removedColNames)
            {
                diff.AppendLine($"ALTER TABLE public.\"{table}\" DROP COLUMN \"{colName}\";");
            }

            var commonColNames = oldColNames.Intersect(newColNames);
            foreach (var colName in commonColNames)
            {
                var oldCol = oldTableCols.First(c => c.ColumnName == colName);
                var newCol = newTableCols.First(c => c.ColumnName == colName);

                if (oldCol.DataType != newCol.DataType || oldCol.CharacterMaximumLength != newCol.CharacterMaximumLength)
                    diff.AppendLine($"ALTER TABLE public.\"{table}\" ALTER COLUMN \"{colName}\" TYPE {newCol.DataType}{(newCol.CharacterMaximumLength != null ? $"({newCol.CharacterMaximumLength})" : "")};");

                if (oldCol.IsNullable == "YES" && newCol.IsNullable == "NO")
                    diff.AppendLine($"ALTER TABLE public.\"{table}\" ALTER COLUMN \"{colName}\" SET NOT NULL;");
                else if (oldCol.IsNullable == "NO" && newCol.IsNullable == "YES")
                    diff.AppendLine($"ALTER TABLE public.\"{table}\" ALTER COLUMN \"{colName}\" DROP NOT NULL;");

                if (oldCol.ColumnDefault != newCol.ColumnDefault)
                {
                    if (string.IsNullOrEmpty(newCol.ColumnDefault))
                        diff.AppendLine($"ALTER TABLE public.\"{table}\" ALTER COLUMN \"{colName}\" DROP DEFAULT;");
                    else
                        diff.AppendLine($"ALTER TABLE public.\"{table}\" ALTER COLUMN \"{colName}\" SET DEFAULT {newCol.ColumnDefault};");
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
        var addedViews = newViews.Keys.Except(oldViews.Keys);
        foreach (var v in addedViews) {
            results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Added", SourceDDL = "-- N/A", TargetDDL = newViews[v], DiffScript = newViews[v] });
        }
        var removedViews = oldViews.Keys.Except(newViews.Keys);
        foreach (var v in removedViews) {
            results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Removed", SourceDDL = oldViews[v], TargetDDL = "-- N/A", DiffScript = $"DROP VIEW IF EXISTS public.{v} CASCADE;" });
        }
        var commonViews = oldViews.Keys.Intersect(newViews.Keys);
        foreach (var v in commonViews) {
            if (oldViews[v] != newViews[v]) {
                results.Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Altered", SourceDDL = oldViews[v], TargetDDL = newViews[v], DiffScript = $"CREATE OR REPLACE VIEW public.{v} AS {newViews[v].Substring(newViews[v].IndexOf(" AS ", StringComparison.OrdinalIgnoreCase) + 4)}" });
            }
        }

        // --- Routine Comparison ---
        var addedRoutines = newRoutines.Keys.Except(oldRoutines.Keys);
        foreach (var r in addedRoutines) {
            results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Added", SourceDDL = "-- N/A", TargetDDL = newRoutines[r], DiffScript = newRoutines[r] });
        }
        var removedRoutines = oldRoutines.Keys.Except(newRoutines.Keys);
        foreach (var r in removedRoutines) {
            results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Removed", SourceDDL = oldRoutines[r], TargetDDL = "-- N/A", DiffScript = $"DROP FUNCTION IF EXISTS {r} CASCADE;" });
        }
        var commonRoutines = oldRoutines.Keys.Intersect(newRoutines.Keys);
        foreach (var r in commonRoutines) {
            if (oldRoutines[r] != newRoutines[r]) {
                results.Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Altered", SourceDDL = oldRoutines[r], TargetDDL = newRoutines[r], DiffScript = newRoutines[r] });
            }
        }

        // --- Index Comparison ---
        await CompareGenericObjectsAsync(results, "Index", oldIndexes, newIndexes);

        // --- Trigger Comparison ---
        await CompareGenericObjectsAsync(results, "Trigger", oldTriggers, newTriggers);

        // --- Constraint Comparison ---
        await CompareGenericObjectsAsync(results, "Constraint", oldConstraints, newConstraints);

        return results;
    }

    private async Task CompareGenericObjectsAsync(List<SchemaDiffResult> results, string type, Dictionary<string, string> oldObjs, Dictionary<string, string> newObjs)
    {
        var added = newObjs.Keys.Except(oldObjs.Keys);
        foreach (var name in added) {
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "Added", SourceDDL = "-- N/A", TargetDDL = newObjs[name], DiffScript = newObjs[name] });
        }
        var removed = oldObjs.Keys.Except(newObjs.Keys);
        foreach (var name in removed) {
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "Removed", SourceDDL = oldObjs[name], TargetDDL = "-- N/A", DiffScript = $"DROP {type.ToUpper()} IF EXISTS {name};" });
        }
        var common = oldObjs.Keys.Intersect(newObjs.Keys);
        foreach (var name in common) {
            if (oldObjs[name] != newObjs[name]) {
                results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = name, DiffType = "Altered", SourceDDL = oldObjs[name], TargetDDL = newObjs[name], DiffScript = newObjs[name] });
            }
        }
    }

    public async Task<string> GenerateSchemaDiffAsync()
    {
        var results = await GenerateSchemaDiffResultsAsync();
        var sb = new StringBuilder();
        sb.AppendLine($"-- Schema update script from {_oldDbConfig.DatabaseName} to {_newDbConfig.DatabaseName}");
        sb.AppendLine($"-- Generated at {DateTime.Now}\n");
        foreach(var r in results) {
            sb.AppendLine($"-- {r.ObjectType}: {r.ObjectName} ({r.DiffType})");
            sb.AppendLine(r.DiffScript);
        }
        return sb.ToString();
    }

    public async Task<string> GenerateDataDiffAsync(List<string> tablesToCompare)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Data update script from {_oldDbConfig.DatabaseName} to {_newDbConfig.DatabaseName}");
        sb.AppendLine($"-- Generated at {DateTime.Now}");
        sb.AppendLine();

        foreach (var table in tablesToCompare)
        {
            var pks = await GetPrimaryKeysAsync(_newDbConfig, table);
            if (!pks.Any())
            {
                sb.AppendLine($"-- WARNING: Table {table} has no primary key. Data comparison skipped.");
                sb.AppendLine();
                continue;
            }

            var oldData = await GetTableDataAsync(_oldDbConfig, table, pks);
            var newData = await GetTableDataAsync(_newDbConfig, table, pks);

            var oldKeys = oldData.Keys.ToList();
            var newKeys = newData.Keys.ToList();

            // Inserted
            var insertedKeys = newKeys.Except(oldKeys).ToList();
            if (insertedKeys.Any())
            {
                foreach (var key in insertedKeys)
                {
                    var row = newData[key];
                    var colNames = string.Join(", ", row.Keys);
                    var colVals = string.Join(", ", row.Values.Select(FormatSqlValue));
                    sb.AppendLine($"INSERT INTO public.\"{table}\" ({colNames}) VALUES ({colVals});");
                }
            }

            // Deleted
            var deletedKeys = oldKeys.Except(newKeys).ToList();
            if (deletedKeys.Any())
            {
                foreach (var key in deletedKeys)
                {
                    var conditions = string.Join(" AND ", pks.Select(pk => $"{pk} = {FormatSqlValue(oldData[key][pk])}"));
                    sb.AppendLine($"DELETE FROM public.\"{table}\" WHERE {conditions};");
                }
            }

            // Updated
            var commonKeys = oldKeys.Intersect(newKeys).ToList();
            foreach (var key in commonKeys)
            {
                var oldRow = oldData[key];
                var newRow = newData[key];
                var updates = new List<string>();

                foreach (var col in newRow.Keys)
                {
                    if (oldRow.ContainsKey(col) && !Equals(oldRow[col], newRow[col]) && !(oldRow[col] is DBNull && newRow[col] is DBNull))
                    {
                        if (oldRow[col] is DBNull || newRow[col] is DBNull || oldRow[col].ToString() != newRow[col].ToString())
                        {
                            updates.Add($"{col} = {FormatSqlValue(newRow[col])}");
                        }
                    }
                }

                if (updates.Any())
                {
                    var conditions = string.Join(" AND ", pks.Select(pk => $"{pk} = {FormatSqlValue(oldRow[pk])}"));
                    sb.AppendLine($"UPDATE public.\"{table}\" SET {string.Join(", ", updates)} WHERE {conditions};");
                }
            }
            if (insertedKeys.Any() || deletedKeys.Any() || commonKeys.Any(k => GetUpdatesForCommonKey(oldData[k], newData[k]).Any()))
                 sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<DataDiffSummary> GetTableDataDiffSummaryAsync(string table)
    {
        var summary = new DataDiffSummary { TableName = table };
        var pks = await GetPrimaryKeysAsync(_newDbConfig, table);
        if (!pks.Any()) return summary;

        var oldData = await GetTableDataAsync(_oldDbConfig, table, pks);
        var newData = await GetTableDataAsync(_newDbConfig, table, pks);

        var oldKeys = oldData.Keys.ToList();
        var newKeys = newData.Keys.ToList();

        summary.InsertedCount = newKeys.Except(oldKeys).Count();
        summary.DeletedCount = oldKeys.Except(newKeys).Count();

        var commonKeys = oldKeys.Intersect(newKeys).ToList();
        foreach (var key in commonKeys)
        {
            if (GetUpdatesForCommonKey(oldData[key], newData[key]).Any())
                summary.UpdatedCount++;
        }

        return summary;
    }

    public async Task<List<DataRowDiff>> GetDetailedTableDataDiffAsync(string table)
    {
        var diffs = new List<DataRowDiff>();
        var pks = await GetPrimaryKeysAsync(_newDbConfig, table);
        if (!pks.Any()) return diffs;

        var oldData = await GetTableDataAsync(_oldDbConfig, table, pks);
        var newData = await GetTableDataAsync(_newDbConfig, table, pks);

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
            var changedCols = GetUpdatesForCommonKey(oldRow, newRow);
            
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

    private List<string> GetUpdatesForCommonKey(Dictionary<string, object> oldRow, Dictionary<string, object> newRow)
    {
        var updates = new List<string>();
        foreach (var col in newRow.Keys)
        {
            if (!oldRow.ContainsKey(col)) continue;

            var v1 = oldRow[col];
            var v2 = newRow[col];

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
        return value.ToString();
    }

    private async Task<List<ColumnInfo>> GetColumnsAsync(DatabaseConfig config)
    {
        var cols = new List<ColumnInfo>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT table_name, column_name, data_type, character_maximum_length, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = 'public'
            ORDER BY table_name, ordinal_position;";

        await using var cmd = new NpgsqlCommand(sql, conn);
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
                ColumnDefault = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
        return cols;
    }

    private async Task<List<string>> GetPrimaryKeysAsync(DatabaseConfig config, string tableName)
    {
        var pks = new List<string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT a.attname
            FROM   pg_index i
            JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE  i.indrelid = CAST('public.' || quote_ident($1) AS regclass) AND i.indisprimary;";

        await using var cmd = new NpgsqlCommand(sql, conn);
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

    private async Task<Dictionary<string, Dictionary<string, object>>> GetTableDataAsync(DatabaseConfig config, string tableName, List<string> primaryKeys)
    {
        var data = new Dictionary<string, Dictionary<string, object>>();
        
        try 
        {
            await using var conn = new NpgsqlConnection(config.GetConnectionString());
            await conn.OpenAsync();

            var sql = $"SELECT * FROM public.\"{tableName}\""; // Assume table exists
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

    private async Task<Dictionary<string, string>> GetViewsAsync(DatabaseConfig config)
    {
        var views = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT table_name, view_definition FROM information_schema.views WHERE table_schema = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var definition = reader.GetString(1);
            views[name] = $"CREATE VIEW public.{name} AS {definition}";
        }
        return views;
    }

    private async Task<Dictionary<string, string>> GetRoutinesAsync(DatabaseConfig config)
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
            WHERE n.nspname = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            routines[reader.GetString(0)] = reader.GetString(1);
        }
        return routines;
    }

    private async Task<Dictionary<string, string>> GetIndexesAsync(DatabaseConfig config)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) dict[reader.GetString(0)] = reader.GetString(1);
        return dict;
    }

    private async Task<Dictionary<string, string>> GetTriggersAsync(DatabaseConfig config)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT t.tgname, pg_get_triggerdef(t.oid) 
            FROM pg_trigger t
            JOIN pg_class c ON t.tgrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE n.nspname = 'public' AND NOT t.tgisinternal;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) dict[reader.GetString(0)] = reader.GetString(1) + ";";
        return dict;
    }

    private async Task<Dictionary<string, string>> GetConstraintsAsync(DatabaseConfig config)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT c.conname, pg_get_constraintdef(c.oid), r.relname
            FROM pg_constraint c
            JOIN pg_namespace n ON c.connamespace = n.oid
            JOIN pg_class r ON c.conrelid = r.oid
            WHERE n.nspname = 'public' AND c.contype IN ('f', 'u', 'c');";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var conname = reader.GetString(0);
            var def = reader.GetString(1);
            var relname = reader.GetString(2);
            dict[conname] = $"ALTER TABLE public.{relname} ADD CONSTRAINT {conname} {def};";
        }
        return dict;
    }

    private class ColumnInfo
    {
        public string? TableName { get; set; }
        public string? ColumnName { get; set; }
        public string? DataType { get; set; }
        public int? CharacterMaximumLength { get; set; }
        public string? IsNullable { get; set; }
        public string? ColumnDefault { get; set; }
    }
}
