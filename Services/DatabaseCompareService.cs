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
    public int SourceRowCount { get; set; }
    public int TargetRowCount { get; set; }
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
    
    public bool IncludeOwner { get; set; } = false;
    public bool IgnoreExtension { get; set; } = true;

    public DatabaseCompareService(DatabaseConfig sourceConfig, DatabaseConfig targetConfig)
    {
        _sourceConfig = sourceConfig;
        _targetConfig = targetConfig;
    }

    public virtual async Task<List<string>> GetSchemasAsync(DatabaseConfig config)
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

    public virtual async Task<List<SchemaDiffResult>> GenerateSchemaDiffResultsAsync(string sourceSchema, string targetSchema)
    {
        var categoryResults = new Dictionary<string, List<SchemaDiffResult>> {
            { "Extension", new() }, { "Role", new() }, { "Enum", new() }, { "Type", new() }, { "Sequence", new() },
            { "Table", new() }, { "View", new() }, { "Routine", new() }, { "Materialized View", new() },
            { "Index", new() }, { "Constraint", new() }, { "Trigger", new() }
        };

        // 1. Get Tables and Columns for both databases
        var targetCols = await GetColumnsAsync(_targetConfig, targetSchema);
        var sourceCols = await GetColumnsAsync(_sourceConfig, sourceSchema);
        var targetTables = targetCols.Select(c => c.TableName).Distinct();
        var sourceTables = sourceCols.Select(c => c.TableName).Distinct();

        // 2. Get Views/Routines
        var targetViews = await GetViewsAsync(_targetConfig, targetSchema);
        var sourceViews = await GetViewsAsync(_sourceConfig, sourceSchema);
        var targetRoutines = await GetRoutinesAsync(_targetConfig, targetSchema);
        var sourceRoutines = await GetRoutinesAsync(_sourceConfig, sourceSchema);

        // 3. Get Other Objects
        var targetIndexes = await GetIndexesAsync(_targetConfig, targetSchema);
        var sourceIndexes = await GetIndexesAsync(_sourceConfig, sourceSchema);
        var targetTriggers = await GetTriggersAsync(_targetConfig, targetSchema);
        var sourceTriggers = await GetTriggersAsync(_sourceConfig, sourceSchema);
        var targetConstraints = await GetConstraintsAsync(_targetConfig, targetSchema);
        var sourceConstraints = await GetConstraintsAsync(_sourceConfig, sourceSchema);
        var targetExtensions = await GetExtensionsAsync(_targetConfig);
        var sourceExtensions = await GetExtensionsAsync(_sourceConfig);
        var targetRoles = await GetRolesAsync(_targetConfig);
        var sourceRoles = await GetRolesAsync(_sourceConfig);
        var targetSequences = await GetSequencesAsync(_targetConfig, targetSchema);
        var sourceSequences = await GetSequencesAsync(_sourceConfig, sourceSchema);
        var targetEnums = await GetEnumsAsync(_targetConfig, targetSchema);
        var sourceEnums = await GetEnumsAsync(_sourceConfig, sourceSchema);
        var targetTypes = await GetCompositeTypesAsync(_targetConfig, targetSchema);
        var sourceTypes = await GetCompositeTypesAsync(_sourceConfig, sourceSchema);
        var targetMatViews = await GetMaterializedViewsAsync(_targetConfig, targetSchema);
        var sourceMatViews = await GetMaterializedViewsAsync(_sourceConfig, sourceSchema);
        var targetOwners = await GetObjectOwnersAsync(_targetConfig, targetSchema);
        var sourceOwners = await GetObjectOwnersAsync(_sourceConfig, sourceSchema);

        // --- Category: Extension ---
        if (!IgnoreExtension)
        {
            foreach (var ext in sourceExtensions.Keys.Except(targetExtensions.Keys))
                categoryResults["Extension"].Add(new SchemaDiffResult { ObjectType = "Extension", ObjectName = ext, DiffType = "Added", SourceDDL = $"CREATE EXTENSION IF NOT EXISTS \"{ext}\" VERSION '{sourceExtensions[ext]}';", TargetDDL = "-- N/A", DiffScript = $"CREATE EXTENSION IF NOT EXISTS \"{ext}\" VERSION '{sourceExtensions[ext]}';" });
            foreach (var ext in targetExtensions.Keys.Intersect(sourceExtensions.Keys))
                if (targetExtensions[ext] != sourceExtensions[ext])
                    categoryResults["Extension"].Add(new SchemaDiffResult { ObjectType = "Extension", ObjectName = ext, DiffType = "Altered", SourceDDL = $"VERSION '{sourceExtensions[ext]}'", TargetDDL = $"VERSION '{targetExtensions[ext]}'", DiffScript = $"ALTER EXTENSION \"{ext}\" UPDATE TO '{sourceExtensions[ext]}';" });
        }

        // --- Category: Role ---
        foreach (var role in sourceRoles.Keys.Except(targetRoles.Keys))
            categoryResults["Role"].Add(new SchemaDiffResult { ObjectType = "Role", ObjectName = role, DiffType = "Added", SourceDDL = sourceRoles[role], TargetDDL = "-- N/A", DiffScript = sourceRoles[role] });

        // --- Category: Enum ---
        foreach (var name in sourceEnums.Keys.Except(targetEnums.Keys))
            categoryResults["Enum"].Add(new SchemaDiffResult { ObjectType = "Enum", ObjectName = name, DiffType = "Added", SourceDDL = sourceEnums[name], TargetDDL = "-- N/A", DiffScript = sourceEnums[name] });

        // --- Category: Type (Composite Types) ---
        // Define set of dropped tables to skip dependent indexes/triggers/constraints later
        var removedTables = targetTables.Except(sourceTables).ToList();
        var targetTableDeps = await GetTableDependenciesAsync(_targetConfig, targetSchema);
        var sortedRemovedTables = SortTablesTopologically(removedTables, targetTableDeps);
        sortedRemovedTables.Reverse(); // Drop child tables first
        var droppedTablesSet = new HashSet<string>(sortedRemovedTables, StringComparer.OrdinalIgnoreCase);

        // --- Category: Type (Composite Types) ---
        await CompareGenericObjectsAsync(categoryResults["Type"], "Type", targetTypes, sourceTypes, targetSchema, sourceOwners, targetOwners, "t", droppedTablesSet);

        // --- Category: Sequence ---
        await CompareGenericObjectsAsync(categoryResults["Sequence"], "Sequence", targetSequences, sourceSequences, targetSchema, sourceOwners, targetOwners, "S", droppedTablesSet);

        // --- Category: Table ---
        var tableDeps = await GetTableDependenciesAsync(_sourceConfig, sourceSchema);
        var addedTables = sourceTables.Except(targetTables).ToList();
        var sortedAddedTables = SortTablesTopologically(addedTables, tableDeps);
        
        foreach (var table in sortedAddedTables)
        {
            var cols = sourceCols.Where(c => c.TableName == table).ToList();
            var ddl = await BuildFullTableDdlAsync(_sourceConfig, sourceSchema, table, cols);
            if (IncludeOwner && sourceOwners.TryGetValue($"r:{table}", out var owner))
            {
                ddl += GetOwnerDdl("r", table, owner, targetSchema);
            }
            categoryResults["Table"].Add(new SchemaDiffResult { ObjectType = "Table", ObjectName = table, DiffType = "Added", SourceDDL = ddl, TargetDDL = "-- Object does not exist in Target database", DiffScript = ddl });
        }

        foreach (var table in sortedRemovedTables)
        {
            var cols = targetCols.Where(c => c.TableName == table).ToList();
            var ddl = await BuildFullTableDdlAsync(_targetConfig, targetSchema, table, cols);
            categoryResults["Table"].Add(new SchemaDiffResult { ObjectType = "Table", ObjectName = table, DiffType = "ExistingInTarget", SourceDDL = ddl, TargetDDL = "-- N/A", DiffScript = $"DROP TABLE IF EXISTS {targetSchema}.\"{table}\" CASCADE;" });
        }

        var commonTables = targetTables.Intersect(sourceTables).ToList();
        foreach (var table in commonTables)
        {
            var targetTableCols = targetCols.Where(c => c.TableName == table).ToList();
            var sourceTableCols = sourceCols.Where(c => c.TableName == table).ToList();
            var diff = new StringBuilder();
            
            // 1. New columns
            foreach (var col in sourceTableCols.Where(sc => !targetTableCols.Any(tc => tc.ColumnName == sc.ColumnName)))
                diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ADD COLUMN \"{col.ColumnName}\" {GetColumnTypeString(col)};");
            
            // 2. Removed columns
            foreach (var colName in targetTableCols.Select(tc => tc.ColumnName).Except(sourceTableCols.Select(sc => sc.ColumnName)))
                diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" DROP COLUMN \"{colName}\";");

            // 3. Changed columns (Deep Comparison)
            var commonCols = sourceTableCols.Where(sc => targetTableCols.Any(tc => tc.ColumnName == sc.ColumnName));
            foreach (var sCol in commonCols)
            {
                var tCol = targetTableCols.First(tc => tc.ColumnName == sCol.ColumnName);
                bool typeChanged = NormalizeTypeName(sCol.DataType) != NormalizeTypeName(tCol.DataType) 
                                   || sCol.CharacterMaximumLength != tCol.CharacterMaximumLength
                                   || sCol.NumericPrecision != tCol.NumericPrecision
                                   || sCol.NumericScale != tCol.NumericScale;
                bool nullChanged = sCol.IsNullable != tCol.IsNullable;
                bool defaultChanged = (sCol.ColumnDefault ?? "") != (tCol.ColumnDefault ?? "");

                if (typeChanged)
                {
                    diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{sCol.ColumnName}\" TYPE {GetColumnTypeString(sCol)};");
                }
                
                if (nullChanged)
                {
                    if (sCol.IsNullable == "NO")
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{sCol.ColumnName}\" SET NOT NULL;");
                    else
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{sCol.ColumnName}\" DROP NOT NULL;");
                }

                if (defaultChanged)
                {
                    if (string.IsNullOrEmpty(sCol.ColumnDefault))
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{sCol.ColumnName}\" DROP DEFAULT;");
                    else
                        diff.AppendLine($"ALTER TABLE {targetSchema}.\"{table}\" ALTER COLUMN \"{sCol.ColumnName}\" SET DEFAULT {sCol.ColumnDefault};");
                }
            }
            
            if (diff.Length > 0 || (IncludeOwner && sourceOwners.TryGetValue($"r:{table}", out var sOwner) && targetOwners.TryGetValue($"r:{table}", out var tOwner) && sOwner != tOwner))
            {
                if (IncludeOwner && sourceOwners.TryGetValue($"r:{table}", out var sO) && targetOwners.TryGetValue($"r:{table}", out var tO) && sO != tO)
                {
                    diff.AppendLine(GetOwnerDdl("r", table, sO, targetSchema).Trim());
                }
                var sDdl = await BuildFullTableDdlAsync(_sourceConfig, sourceSchema, table, sourceTableCols);
                var tDdl = await BuildFullTableDdlAsync(_targetConfig, targetSchema, table, targetTableCols);

                categoryResults["Table"].Add(new SchemaDiffResult { 
                    ObjectType = "Table", ObjectName = table, DiffType = "Altered", 
                    SourceDDL = sDdl, TargetDDL = tDdl, DiffScript = diff.ToString().Trim() 
                });
            }
        }

        // --- Category: View ---
        var addedViews = sourceViews.Keys.Except(targetViews.Keys);
        foreach (var v in addedViews) {
            string sourceDef = sourceViews[v].Replace(sourceSchema + ".", targetSchema + ".");
            string diffScript = sourceDef;
            if (IncludeOwner && sourceOwners.TryGetValue($"v:{v}", out var owner))
                diffScript += GetOwnerDdl("v", v, owner, targetSchema);
            categoryResults["View"].Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "Added", SourceDDL = diffScript, TargetDDL = "-- Object does not exist in Target database", DiffScript = diffScript });
        }
        var removedViews = targetViews.Keys.Except(sourceViews.Keys);
        foreach (var v in removedViews) {
            categoryResults["View"].Add(new SchemaDiffResult { ObjectType = "View", ObjectName = v, DiffType = "ExistingInTarget", SourceDDL = targetViews[v], TargetDDL = "-- N/A", DiffScript = $"DROP VIEW IF EXISTS {targetSchema}.{v};" });
        }
        var commonViews = targetViews.Keys.Intersect(sourceViews.Keys);
        foreach (var v in commonViews) {
            string sourceDef = sourceViews[v].Replace(sourceSchema + ".", targetSchema + ".");
            bool viewChanged = targetViews[v] != sourceDef;
            string? sO = null;
            bool ownerChanged = IncludeOwner && sourceOwners.TryGetValue($"v:{v}", out sO) && targetOwners.TryGetValue($"v:{v}", out var tO) && sO != tO;
            if (viewChanged || ownerChanged) {
                string diffScript = $"CREATE OR REPLACE VIEW {targetSchema}.{v} AS {sourceDef}";
                if (ownerChanged)
                    diffScript += GetOwnerDdl("v", v, sO!, targetSchema);
                categoryResults["View"].Add(new SchemaDiffResult { 
                    ObjectType = "View", ObjectName = v, DiffType = "Altered", 
                    SourceDDL = sourceDef, TargetDDL = targetViews[v],
                    DiffScript = diffScript
                });
            }
        }

        // --- Category: Routine ---
        var addedRoutines = sourceRoutines.Keys.Except(targetRoutines.Keys);
        foreach (var r in addedRoutines) {
            string sourceDef = sourceRoutines[r].Replace(sourceSchema + ".", targetSchema + ".");
            string diffScript = sourceDef;
            if (IncludeOwner && sourceOwners.TryGetValue($"f:{r}", out var owner))
                diffScript += GetOwnerDdl("f", r, owner, targetSchema);
            categoryResults["Routine"].Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Added", SourceDDL = diffScript, TargetDDL = "-- Object does not exist in Target database", DiffScript = diffScript });
        }
        var removedRoutines = targetRoutines.Keys.Except(sourceRoutines.Keys);
        foreach (var r in removedRoutines) {
            categoryResults["Routine"].Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "ExistingInTarget", SourceDDL = targetRoutines[r], TargetDDL = "-- N/A", DiffScript = $"DROP FUNCTION IF EXISTS {targetSchema}.{r};" });
        }
        var commonRoutines = targetRoutines.Keys.Intersect(sourceRoutines.Keys);
        foreach (var r in commonRoutines) {
            string sourceDef = sourceRoutines[r].Replace(sourceSchema + ".", targetSchema + ".");
            bool routineChanged = targetRoutines[r] != sourceDef;
            string? sO = null;
            bool ownerChanged = IncludeOwner && sourceOwners.TryGetValue($"f:{r}", out sO) && targetOwners.TryGetValue($"f:{r}", out var tO) && sO != tO;
            if (routineChanged || ownerChanged) {
                string diffScript = sourceDef;
                if (ownerChanged)
                    diffScript += GetOwnerDdl("f", r, sO!, targetSchema);
                categoryResults["Routine"].Add(new SchemaDiffResult { ObjectType = "Routine", ObjectName = r, DiffType = "Altered", SourceDDL = sourceDef, TargetDDL = targetRoutines[r], DiffScript = diffScript });
            }
        }

        // --- Category: Materialized View ---
        foreach (var name in sourceMatViews.Keys.Except(targetMatViews.Keys)) {
            string sourceDef = sourceMatViews[name].Replace(sourceSchema + ".", targetSchema + ".");
            string diffScript = sourceDef;
            if (IncludeOwner && sourceOwners.TryGetValue($"m:{name}", out var owner))
                diffScript += GetOwnerDdl("m", name, owner, targetSchema);
            categoryResults["Materialized View"].Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "Added", SourceDDL = diffScript, TargetDDL = "-- Object does not exist in Target database", DiffScript = diffScript });
        }
        var removedMatViews = targetMatViews.Keys.Except(sourceMatViews.Keys);
        foreach (var name in removedMatViews) {
            categoryResults["Materialized View"].Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "ExistingInTarget", SourceDDL = targetMatViews[name], TargetDDL = "-- N/A", DiffScript = $"DROP MATERIALIZED VIEW IF EXISTS {targetSchema}.{name};" });
        }
        var commonMatViews = targetMatViews.Keys.Intersect(sourceMatViews.Keys);
        foreach (var name in commonMatViews) {
            string sourceDef = sourceMatViews[name].Replace(sourceSchema + ".", targetSchema + ".");
            bool viewChanged = targetMatViews[name] != sourceDef;
            string? sO = null;
            bool ownerChanged = IncludeOwner && sourceOwners.TryGetValue($"m:{name}", out sO) && targetOwners.TryGetValue($"m:{name}", out var tO) && sO != tO;
            if (viewChanged || ownerChanged) {
                string diffScript = $"DROP MATERIALIZED VIEW IF EXISTS {targetSchema}.{name};\n{sourceDef}";
                if (ownerChanged)
                    diffScript += GetOwnerDdl("m", name, sO!, targetSchema);
                categoryResults["Materialized View"].Add(new SchemaDiffResult { ObjectType = "Materialized View", ObjectName = name, DiffType = "Altered", SourceDDL = sourceDef, TargetDDL = targetMatViews[name], DiffScript = diffScript });
            }
        }

        // --- Categories: Index, Constraint, Trigger ---
        await CompareGenericObjectsAsync(categoryResults["Index"], "Index", targetIndexes, sourceIndexes, targetSchema, droppedTables: droppedTablesSet);
        await CompareGenericObjectsAsync(categoryResults["Trigger"], "Trigger", targetTriggers, sourceTriggers, targetSchema, droppedTables: droppedTablesSet);
        await CompareGenericObjectsAsync(categoryResults["Constraint"], "Constraint", targetConstraints, sourceConstraints, targetSchema, droppedTables: droppedTablesSet);

        // Combine all in logical order
        var finalResults = new List<SchemaDiffResult>();
        string[] order = { "Extension", "Role", "Enum", "Type", "Sequence", "Table", "View", "Routine", "Materialized View", "Index", "Constraint", "Trigger" };
        foreach (var cat in order) finalResults.AddRange(categoryResults[cat]);
        
        return finalResults;
    }

    internal string MapPostgresType(string type)
    {
        if (string.IsNullOrEmpty(type)) return "";
        return type.ToLower().Trim() switch
        {
            "varchar" => "character varying",
            "int4" => "integer",
            "int8" => "bigint",
            "bool" => "boolean",
            "timestamp" => "timestamp without time zone",
            _ => type
        };
    }

    private string NormalizeTypeName(string type)
    {
        if (string.IsNullOrEmpty(type)) return "";
        return type.ToLower().Trim() switch
        {
            "varchar" => "character varying",
            "int4" => "integer",
            "int" => "integer",
            "int8" => "bigint",
            "bool" => "boolean",
            "timestamp" => "timestamp without time zone",
            "numeric" => "numeric",
            "decimal" => "numeric",
            _ => type.ToLower().Trim()
        };
    }

    private string GetColumnTypeString(ColumnInfo col)
    {
        string typeStr = MapPostgresType(col.DataType);
        if (col.CharacterMaximumLength != null)
        {
            typeStr += $"({col.CharacterMaximumLength})";
        }
        else if (col.NumericPrecision != null && NormalizeTypeName(col.DataType) == "numeric")
        {
            if (col.NumericScale != null)
                typeStr += $"({col.NumericPrecision},{col.NumericScale})";
            else
                typeStr += $"({col.NumericPrecision})";
        }
        return typeStr;
    }

    private async Task<string> BuildFullTableDdlAsync(DatabaseConfig config, string schema, string table, List<ColumnInfo> cols)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {schema}.\"{table}\" (");
        
        var lines = new List<string>();
        foreach (var c in cols)
        {
            // Simplified names without quotes as per user sample
            var def = $"    {c.ColumnName} {GetColumnTypeString(c)}";
            if (!string.IsNullOrEmpty(c.ColumnDefault)) def += $" DEFAULT {c.ColumnDefault}";
            def += (c.IsNullable == "NO" ? " NOT NULL" : " NULL");
            lines.Add(def);
        }

        // Add Primary Key
        var pks = await GetPrimaryKeysAsync(config, table, schema);
        if (pks.Any())
        {
            // Simplified PK name: strip common prefixes like U01_
            string workingName = table.ToLower();
            if (workingName.Length > 4 && workingName.StartsWith("u") && char.IsDigit(workingName[1]) && char.IsDigit(workingName[2]) && workingName[3] == '_')
            {
                workingName = workingName.Substring(4);
            }
            
            string pkName = $"pk_{workingName}";
            lines.Add($"    CONSTRAINT {pkName} PRIMARY KEY ({string.Join(", ", pks)})");
        }

        sb.AppendLine(string.Join(",\n", lines));
        sb.AppendLine(");");
        return sb.ToString();
    }

    private async Task CompareGenericObjectsAsync(List<SchemaDiffResult> results, string type, Dictionary<string, string> oldObjs, Dictionary<string, string> newObjs, string targetSchema, Dictionary<string, string>? sourceOwners = null, Dictionary<string, string>? targetOwners = null, string? kind = null, HashSet<string>? droppedTables = null)
    {
        var added = newObjs.Keys.Except(oldObjs.Keys);
        foreach (var name in added) {
            string diffScript = newObjs[name];
            if (IncludeOwner && sourceOwners != null && kind != null && sourceOwners.TryGetValue($"{kind}:{name}", out var owner))
            {
                diffScript += GetOwnerDdl(kind, name, owner, targetSchema);
            }
            string displayName = name;
            if (type == "Constraint" && name.Contains(":"))
            {
                var parts = name.Split(':');
                displayName = $"{parts[1]} [{parts[0]}]";
            }
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = displayName, DiffType = "Added", SourceDDL = diffScript, TargetDDL = "-- Object does not exist in Target database", DiffScript = diffScript });
        }
        var removed = oldObjs.Keys.Except(newObjs.Keys);
        foreach (var name in removed) {
            // Extract table name from the object definition to check if its parent table is dropped
            string parentTable = "";
            string def = oldObjs[name];
            
            if (type == "Index" || type == "Trigger") {
                var match = System.Text.RegularExpressions.Regex.Match(def, @"\bON\s+(?:[a-zA-Z0-9_""\.]+\.)?""?([a-zA-Z0-9_]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) {
                    parentTable = match.Groups[1].Value;
                }
            } else if (type == "Constraint") {
                var match = System.Text.RegularExpressions.Regex.Match(def, @"ALTER TABLE\s+(?:[a-zA-Z0-9_""\.]+\.)?""?([a-zA-Z0-9_]+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) {
                    parentTable = match.Groups[1].Value;
                }
            }

            if (!string.IsNullOrEmpty(parentTable) && droppedTables != null && droppedTables.Contains(parentTable)) {
                // Skip outputting drop statement for this dependent object since its parent table is being dropped with CASCADE
                continue;
            }

            string dropScript = "";
            string displayName = name;
            if (type == "Constraint") {
                string constraintName = name;
                if (name.Contains(":"))
                {
                    var parts = name.Split(':');
                    constraintName = parts[1];
                    displayName = $"{parts[1]} [{parts[0]}]";
                }
                var match = System.Text.RegularExpressions.Regex.Match(oldObjs[name], @"ALTER TABLE\s+(\S+)\s+ADD CONSTRAINT", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) {
                    string tableName = match.Groups[1].Value;
                    dropScript = $"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS \"{constraintName}\";";
                } else {
                    dropScript = $"-- Constraint \"{constraintName}\" was removed from Source. Could not detect table to drop automatically.";
                }
            } else if (type == "Trigger") {
                var match = System.Text.RegularExpressions.Regex.Match(oldObjs[name], @"\bON\s+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) {
                    string tableName = match.Groups[1].Value;
                    dropScript = $"DROP TRIGGER IF EXISTS \"{name}\" ON {tableName};";
                } else {
                    dropScript = $"-- Trigger \"{name}\" was removed from Source. Could not detect table to drop automatically.";
                }
            } else if (type == "Index") {
                dropScript = $"DROP INDEX IF EXISTS {targetSchema}.\"{name}\";";
            } else if (type == "Sequence") {
                dropScript = $"DROP SEQUENCE IF EXISTS {targetSchema}.\"{name}\" CASCADE;";
            } else if (type == "Enum" || type == "Type") {
                dropScript = $"DROP TYPE IF EXISTS {targetSchema}.\"{name}\" CASCADE;";
            } else {
                dropScript = $"DROP {type.ToUpper()} IF EXISTS {name};";
            }
            results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = displayName, DiffType = "ExistingInTarget", SourceDDL = oldObjs[name], TargetDDL = "-- N/A", DiffScript = dropScript });
        }
        var common = oldObjs.Keys.Intersect(newObjs.Keys);
        foreach (var name in common) {
            bool bodyChanged = oldObjs[name] != newObjs[name];
            bool ownerChanged = IncludeOwner && sourceOwners != null && targetOwners != null && kind != null 
                                && sourceOwners.TryGetValue($"{kind}:{name}", out var sOwner) 
                                && targetOwners.TryGetValue($"{kind}:{name}", out var tOwner) 
                                && sOwner != tOwner;
            
            if (bodyChanged || ownerChanged) {
                string diffScript = newObjs[name];
                if (ownerChanged)
                {
                    sourceOwners.TryGetValue($"{kind}:{name}", out var sO);
                    diffScript += GetOwnerDdl(kind, name, sO!, targetSchema);
                }
                string displayName = name;
                if (type == "Constraint" && name.Contains(":"))
                {
                    var parts = name.Split(':');
                    displayName = $"{parts[1]} [{parts[0]}]";
                }
                results.Add(new SchemaDiffResult { ObjectType = type, ObjectName = displayName, DiffType = "Altered", SourceDDL = diffScript, TargetDDL = oldObjs[name], DiffScript = diffScript });
            }
        }
    }

    public virtual async Task<string> GenerateSchemaDiffAsync(string sourceSchema, string targetSchema)
    {
        var results = await GenerateSchemaDiffResultsAsync(sourceSchema, targetSchema);
        var sb = new StringBuilder();
        sb.AppendLine($"-- Schema update script from {_sourceConfig.DatabaseName} to {_targetConfig.DatabaseName} (Source Schema: {sourceSchema}, Target Schema: {targetSchema})");
        sb.AppendLine($"-- Generated at {DateTime.Now}\n");
        sb.AppendLine("BEGIN;\n");
        foreach(var r in results) {
            sb.AppendLine(r.DiffScript);
        }
        sb.AppendLine("COMMIT;");
        return sb.ToString();
    }

    public virtual async Task<string> GenerateDataDiffAsync(List<string> tablesToCompare, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
    {
        options ??= new DataCompareOptions();
        var sb = new StringBuilder();
        sb.AppendLine($"-- Data update script from {_sourceConfig.DatabaseName} to {_targetConfig.DatabaseName} (Source Schema: {sourceSchema}, Target Schema: {targetSchema})");
        sb.AppendLine($"-- Generated at {DateTime.Now}");
        if (!string.IsNullOrEmpty(options.WhereClause)) sb.AppendLine($"-- Filter: {options.WhereClause}");
        if (options.IgnoreColumns.Any()) sb.AppendLine($"-- Ignoring: {string.Join(", ", options.IgnoreColumns)}");
        sb.AppendLine();

        // 1. Analyze dependencies and sort tables
        var deps = await GetTableDependenciesAsync(_sourceConfig, sourceSchema);
        var sortedTables = SortTablesTopologically(tablesToCompare, deps);

        // 2. PASS 1: Deletes (Reverse order: Child tables first)
        sb.AppendLine("-- === STEP 1: DELETES (Child to Parent) ===");
        var reversedTables = new List<string>(sortedTables);
        reversedTables.Reverse();
        foreach (var table in reversedTables)
        {
            var pks = await GetPrimaryKeysAsync(_sourceConfig, table, sourceSchema);
            if (!pks.Any()) continue;

            var oldData = await GetTableDataAsync(_targetConfig, table, pks, targetSchema, options.WhereClause);
            var newData = await GetTableDataAsync(_sourceConfig, table, pks, sourceSchema, options.WhereClause);
            var deletedKeys = oldData.Keys.Except(newData.Keys).ToList();

            if (deletedKeys.Any())
            {
                sb.AppendLine($"-- Table {table}: Deleting {deletedKeys.Count} records");
                foreach (var key in deletedKeys)
                {
                    var conditions = string.Join(" AND ", pks.Select(pk => $"\"{pk}\" = {FormatSqlValue(oldData[key][pk])}"));
                    sb.AppendLine($"DELETE FROM {targetSchema}.\"{table}\" WHERE {conditions};");
                }
                sb.AppendLine();
            }
        }

        // 3. PASS 2: Inserts and Updates (Forward order: Parent tables first)
        sb.AppendLine("-- === STEP 2: INSERTS & UPDATES (Parent to Child) ===");
        foreach (var table in sortedTables)
        {
            var pks = await GetPrimaryKeysAsync(_sourceConfig, table, sourceSchema);
            if (!pks.Any()) continue;

            var oldData = await GetTableDataAsync(_targetConfig, table, pks, targetSchema, options.WhereClause);
            var newData = await GetTableDataAsync(_sourceConfig, table, pks, sourceSchema, options.WhereClause);
            
            var insertedKeys = newData.Keys.Except(oldData.Keys).ToList();
            var commonKeys = newData.Keys.Intersect(oldData.Keys).ToList();

            if (insertedKeys.Any() || commonKeys.Any(k => GetUpdatesForCommonKey(newData[k], oldData[k], options.IgnoreColumns).Any()))
            {
                sb.AppendLine($"-- Table {table}: Processing Inserts/Updates");
                
                // Inserts
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

                // Updates
                foreach (var key in commonKeys)
                {
                    var oldRow = oldData[key];
                    var newRow = newData[key];
                    var changedCols = GetUpdatesForCommonKey(newRow, oldRow, options.IgnoreColumns);

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
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public virtual async Task<DataDiffSummary> GetTableDataDiffSummaryAsync(string table, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
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
        summary.SourceRowCount = newData.Count;
        summary.TargetRowCount = oldData.Count;

        var commonKeys = oldKeys.Intersect(newKeys).ToList();
        foreach (var key in commonKeys)
        {
            if (GetUpdatesForCommonKey(oldData[key], newData[key], options.IgnoreColumns).Any())
                summary.UpdatedCount++;
        }

        return summary;
    }

    public virtual async Task<List<DataRowDiff>> GetDetailedTableDataDiffAsync(string table, string sourceSchema, string targetSchema, DataCompareOptions? options = null)
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
        if (value is Guid g) return $"'{g}'";
        if (value is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
        if (value is bool b) return b ? "TRUE" : "FALSE";
        if (value is System.Collections.IEnumerable && !(value is string))
        {
            // Handle arrays (simplified)
            return $"'{{{string.Join(",", ((System.Collections.IEnumerable)value).Cast<object>().Select(v => v.ToString() ?? ""))}}}'";
        }
        
        // Check if value is a numeric type
        if (value is int || value is long || value is short || value is byte ||
            value is float || value is double || value is decimal ||
            value is uint || value is ulong || value is ushort)
        {
            return value.ToString() ?? "";
        }

        // Handle potential JSONB (if string looks like JSON)
        var str = value.ToString() ?? "";
        if ((str.StartsWith("{") && str.EndsWith("}")) || (str.StartsWith("[") && str.EndsWith("]")))
            return $"'{str.Replace("'", "''")}'::jsonb";
            
        return $"'{str.Replace("'", "''")}'";
    }

    private async Task<List<ColumnInfo>> GetColumnsAsync(DatabaseConfig config, string schemaName)
    {
        var cols = new List<ColumnInfo>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT c.table_name, c.column_name, c.data_type, c.character_maximum_length, c.is_nullable, c.column_default, c.numeric_precision, c.numeric_scale
            FROM information_schema.columns c
            JOIN information_schema.tables t ON c.table_name = t.table_name AND c.table_schema = t.table_schema
            JOIN pg_class tc ON tc.relname = t.table_name
            JOIN pg_namespace n ON n.oid = tc.relnamespace AND n.nspname = t.table_schema
            WHERE c.table_schema = $1 AND t.table_type = 'BASE TABLE'" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_class'::regclass 
                    AND d.objid = tc.oid 
                    AND d.deptype = 'e'
              )" : "") + @"
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
                ColumnDefault = reader.IsDBNull(5) ? "" : reader.GetString(5),
                NumericPrecision = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                NumericScale = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7)
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
        var sql = @"
            SELECT v.table_name, v.view_definition 
            FROM information_schema.views v
            JOIN pg_class c ON c.relname = v.table_name
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = v.table_schema
            WHERE v.table_schema = $1" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_class'::regclass 
                    AND d.objid = c.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
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
            WHERE n.nspname = $1" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_proc'::regclass 
                    AND d.objid = p.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            routines[reader.GetString(0)] = reader.GetString(1) + ";";
        }
        return routines;
    }

    private async Task<Dictionary<string, string>> GetIndexesAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT 
                i.relname AS indexname,
                pg_get_indexdef(i.oid) || ';' AS indexdef
            FROM pg_index x
            JOIN pg_class c ON c.oid = x.indrelid
            JOIN pg_class i ON i.oid = x.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_constraint con ON con.conindid = i.oid
            WHERE n.nspname = $1 
              AND con.oid IS NULL
              AND NOT x.indisprimary" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_class'::regclass 
                    AND d.objid = i.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
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
            WHERE n.nspname = $1 AND NOT t.tgisinternal" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_trigger'::regclass 
                    AND d.objid = t.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
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
            WHERE n.nspname = $1 AND c.contype IN ('f', 'u', 'c')" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_constraint'::regclass 
                    AND d.objid = c.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var conname = reader.GetString(0);
            var def = reader.GetString(1);
            var relname = reader.GetString(2);
            dict[$"{relname}:{conname}"] = $"ALTER TABLE {schemaName}.{relname} ADD CONSTRAINT {conname} {def};";
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
            WHERE c.relkind = 'S' AND n.nspname = $1" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_class'::regclass 
                    AND d.objid = c.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
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
              AND t.typtype = 'e'" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_type'::regclass 
                    AND d.objid = t.oid 
                    AND d.deptype = 'e'
              )" : "") + @"
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

    private async Task<Dictionary<string, string>> GetCompositeTypesAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT
                t.typname AS type_name,
                a.attname AS attribute_name,
                pg_catalog.format_type(a.atttypid, a.atttypmod) AS attribute_type
            FROM pg_type t
            JOIN pg_class c ON t.typrelid = c.oid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            LEFT JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE c.relkind = 'c'
              AND n.nspname = $1" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_type'::regclass 
                    AND d.objid = t.oid 
                    AND d.deptype = 'e'
              )" : "") + @"
            ORDER BY t.typname, a.attnum;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        var typeFields = new Dictionary<string, List<string>>();
        while (await reader.ReadAsync())
        {
            var typeName = reader.GetString(0);
            if (!typeFields.ContainsKey(typeName))
            {
                typeFields[typeName] = new List<string>();
            }
            if (!reader.IsDBNull(1))
            {
                var attrName = reader.GetString(1);
                var attrType = reader.GetString(2);
                // Map the Postgres type of composite type attributes to standard names as well
                typeFields[typeName].Add($"    \"{attrName}\" {MapPostgresType(attrType)}");
            }
        }

        foreach (var kvp in typeFields)
        {
            var typeName = kvp.Key;
            var fields = string.Join(",\n", kvp.Value);
            dict[typeName] = $"CREATE TYPE {schemaName}.\"{typeName}\" AS (\n{fields}\n);";
        }
        return dict;
    }

    private async Task<Dictionary<string, string>> GetMaterializedViewsAsync(DatabaseConfig config, string schemaName)
    {
        var dict = new Dictionary<string, string>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT matviewname, definition 
            FROM pg_matviews mv
            JOIN pg_class c ON c.relname = mv.matviewname
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = mv.schemaname
            WHERE mv.schemaname = $1" + (IgnoreExtension ? @"
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d 
                  WHERE d.classid = 'pg_class'::regclass 
                    AND d.objid = c.oid 
                    AND d.deptype = 'e'
              )" : "") + ";";
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

    private async Task<List<(string Table, string DependsOn)>> GetTableDependenciesAsync(DatabaseConfig config, string schemaName)
    {
        var deps = new List<(string Table, string DependsOn)>();
        await using var conn = new NpgsqlConnection(config.GetConnectionString());
        await conn.OpenAsync();
        var sql = @"
            SELECT 
                c1.relname as table_name,
                c2.relname as foreign_table_name
            FROM 
                pg_constraint con
            JOIN pg_class c1 ON con.conrelid = c1.oid
            JOIN pg_class c2 ON con.confrelid = c2.oid
            JOIN pg_namespace n ON con.connamespace = n.oid
            WHERE 
                con.contype = 'f' 
                AND n.nspname = $1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            deps.Add((reader.GetString(0), reader.GetString(1)));
        }
        return deps;
    }

    internal List<string> SortTablesTopologically(IEnumerable<string> tables, List<(string Table, string DependsOn)> dependencies)
    {
        var sorted = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var tableSet = new HashSet<string>(tables);

        void Visit(string table)
        {
            if (visited.Contains(table)) return;
            if (visiting.Contains(table)) return; // Cycle detected, ignore for simple sort

            visiting.Add(table);
            foreach (var dep in dependencies.Where(d => d.Table == table && tableSet.Contains(d.DependsOn)))
            {
                Visit(dep.DependsOn);
            }
            visiting.Remove(table);
            visited.Add(table);
            sorted.Add(table);
        }

        foreach (var t in tables) Visit(t);
        return sorted;
    }

    private async Task<Dictionary<string, string>> GetObjectOwnersAsync(DatabaseConfig config, string schemaName)
    {
        var owners = new Dictionary<string, string>();
        if (!IncludeOwner) return owners;
        
        try
        {
            await using var conn = new NpgsqlConnection(config.GetConnectionString());
            await conn.OpenAsync();

            // 1. Tables & Views & Materialized Views & Sequences owners
            var sqlClass = @"
                SELECT 
                    c.relname AS name,
                    r.rolname AS owner,
                    c.relkind::text
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_roles r ON c.relowner = r.oid
                WHERE n.nspname = $1 AND c.relkind IN ('r', 'v', 'm', 'S');";
            await using (var cmd = new NpgsqlCommand(sqlClass, conn))
            {
                cmd.Parameters.AddWithValue(schemaName);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(0);
                    var owner = reader.GetString(1);
                    var kind = reader.GetString(2);
                    string key = $"{kind}:{name}";
                    owners[key] = owner;
                }
            }

            // 2. Routines (Functions/Procedures) owners
            var sqlProc = @"
                SELECT 
                    p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')' AS signature, 
                    r.rolname AS owner
                FROM pg_proc p
                JOIN pg_namespace n ON p.pronamespace = n.oid
                JOIN pg_roles r ON p.proowner = r.oid
                WHERE n.nspname = $1;";
            await using (var cmd = new NpgsqlCommand(sqlProc, conn))
            {
                cmd.Parameters.AddWithValue(schemaName);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var sig = reader.GetString(0);
                    var owner = reader.GetString(1);
                    owners[$"f:{sig}"] = owner;
                }
            }

            // 3. Composite Types owners
            var sqlType = @"
                SELECT 
                    t.typname AS name, 
                    r.rolname AS owner
                FROM pg_type t
                JOIN pg_namespace n ON t.typnamespace = n.oid
                JOIN pg_roles r ON t.typowner = r.oid
                WHERE n.nspname = $1;";
            await using (var cmd = new NpgsqlCommand(sqlType, conn))
            {
                cmd.Parameters.AddWithValue(schemaName);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(0);
                    var owner = reader.GetString(1);
                    owners[$"t:{name}"] = owner;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return owners;
    }

    private string GetOwnerDdl(string kind, string objectName, string owner, string schemaName)
    {
        return kind switch
        {
            "r" => $"\nALTER TABLE IF EXISTS {schemaName}.\"{objectName}\" OWNER TO \"{owner}\";",
            "v" => $"\nALTER VIEW IF EXISTS {schemaName}.\"{objectName}\" OWNER TO \"{owner}\";",
            "m" => $"\nALTER TABLE IF EXISTS {schemaName}.\"{objectName}\" OWNER TO \"{owner}\";", 
            "S" => $"\nALTER SEQUENCE IF EXISTS {schemaName}.\"{objectName}\" OWNER TO \"{owner}\";",
            "f" => $"\nALTER FUNCTION {schemaName}.{objectName} OWNER TO \"{owner}\";", 
            "t" => $"\nALTER TYPE {schemaName}.\"{objectName}\" OWNER TO \"{owner}\";",
            _ => ""
        };
    }

    private class ColumnInfo
    {
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public string DataType { get; set; } = "";
        public int? CharacterMaximumLength { get; set; }
        public string IsNullable { get; set; } = "";
        public string ColumnDefault { get; set; } = "";
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
    }
}
