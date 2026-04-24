using ReleasePrepTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReleasePrepTool.Services;

public class JunkAnalysisService
{
    private readonly PostgresService _pgService;

    public JunkAnalysisService(PostgresService pgService)
    {
        _pgService = pgService;
    }

    public async Task<List<JunkAnalysisResult>> AnalyzeAsync(string[] databases, List<string> keywords, List<SchemaSelection>? schemaSelections = null)
    {
        var results = new List<JunkAnalysisResult>();
        var selectionDict = schemaSelections?.ToDictionary(s => s.SchemaName, s => s, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SchemaSelection>(StringComparer.OrdinalIgnoreCase);

        foreach (var dbName in databases)
        {
            var dbResult = new JunkAnalysisResult { DatabaseName = dbName };
            try
            {
                    var schemasWithOid = await _pgService.GetSchemasAsync(dbName);
                    Console.WriteLine($"[ANALYSIS] Found {schemasWithOid.Count} schemas in database '{dbName}'");
                    foreach (var (schema, schemaOid) in schemasWithOid)
                    {
                        Console.WriteLine($"[ANALYSIS] Processing Schema: {schema}");
                        if (schemaSelections != null && !selectionDict.ContainsKey(schema)) continue;
                        var selection = selectionDict.GetValueOrDefault(schema) ?? new SchemaSelection { SchemaName = schema };

                        var schemaComment = await _pgService.GetObjectCommentAsync(dbName, schema, schema, JunkType.Schema);
                        if (IsJunk(schema, keywords, out _) || IsJunk(schemaComment, keywords, out _)) {
                            if (selection.IncludeStructure)
                                await ProcessJunk(dbResult, dbName, schema, schema, null, JunkType.Schema, schemaOid, null, schemaComment, keywords);
                            continue;
                        }

                        if (selection.IncludeStructure)
                        {
                            Console.WriteLine($"[ANALYSIS] Scanning structure in schema '{schema}'...");
                            // Tables
                            try {
                                var tables = await _pgService.GetSchemaTablesAsync(dbName, schema);
                                if (tables != null) {
                                    var tableColumnsRes = await _pgService.GetSchemaColumnsAsync(dbName, schema);
                                    
                                    // DEBUG: log all raw columns returned by GetSchemaColumnsAsync
                                    Console.WriteLine($"[SCAN-DEBUG] GetSchemaColumnsAsync returned {tableColumnsRes.Count} rows for schema '{schema}':");
                                    foreach (var rawCol in tableColumnsRes)
                                        Console.WriteLine($"  [RAW-COL] TableOid={rawCol.TableOid}, TableName={rawCol.TableName}, Column={rawCol.Column}");

                                    foreach (var (table, oid) in tables) {
                                        var tableColumns = tableColumnsRes.Where(c => c.TableOid == oid).Select(c => c.Column).ToList();
                                        // DEBUG: show OID and matched columns for EVERY table
                                        Console.WriteLine($"[SCAN-DEBUG] Table '{table}' (OID={oid}) -> matched {tableColumns.Count} cols from GetSchemaColumnsAsync: [{string.Join(", ", tableColumns)}]");
                                        
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, table, JunkType.Table);
                                        await ProcessJunk(dbResult, dbName, schema, table, null, JunkType.Table, oid, null, comment, keywords, tableColumns);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan tables in {schema}: {ex.Message}"); }

                            // Views
                            try {
                                var views = await _pgService.GetSchemaViewsAsync(dbName, schema);
                                if (views != null) {
                                    foreach (var (view, oid) in views) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, view, JunkType.View, oid);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, view, JunkType.View);
                                        await ProcessJunk(dbResult, dbName, schema, view, null, JunkType.View, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan views in {schema}: {ex.Message}"); }

                            // Routines
                            try {
                                var routines = await _pgService.GetSchemaRoutinesAsync(dbName, schema);
                                if (routines != null) {
                                    foreach (var (routine, oid) in routines) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, routine, JunkType.Routine);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, routine, JunkType.Routine);
                                        await ProcessJunk(dbResult, dbName, schema, routine, null, JunkType.Routine, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan routines in {schema}: {ex.Message}"); }

                            // Indexes
                            try {
                                var indexes = await _pgService.GetSchemaIndexesAsync(dbName, schema);
                                if (indexes != null) {
                                    foreach (var (idx, oid) in indexes) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, idx, JunkType.Index);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, idx, JunkType.Index);
                                        await ProcessJunk(dbResult, dbName, schema, idx, null, JunkType.Index, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan indexes in {schema}: {ex.Message}"); }

                            // Triggers
                            try {
                                var triggers = await _pgService.GetSchemaTriggersAsync(dbName, schema);
                                if (triggers != null) {
                                    foreach (var (table, trigger, oid) in triggers) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, trigger, JunkType.Trigger);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, trigger, JunkType.Trigger);
                                        await ProcessJunk(dbResult, dbName, schema, trigger, table, JunkType.Trigger, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan triggers in {schema}: {ex.Message}"); }

                            // Constraints
                            try {
                                var constraints = await _pgService.GetSchemaConstraintsAsync(dbName, schema);
                                if (constraints != null) {
                                    foreach (var (table, con, oid) in constraints) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, con, JunkType.Constraint);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, con, JunkType.Constraint);
                                        await ProcessJunk(dbResult, dbName, schema, con, table, JunkType.Constraint, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan constraints in {schema}: {ex.Message}"); }

                            // Data Types
                            try {
                                var types = await _pgService.GetSchemaTypesAsync(dbName, schema);
                                if (types != null) {
                                    foreach (var (typ, oid) in types) {
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, typ, JunkType.DataType);
                                        await ProcessJunk(dbResult, dbName, schema, typ, null, JunkType.DataType, oid, null, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan types in {schema}: {ex.Message}"); }
 
                            // Domains
                            try {
                                var domains = await _pgService.GetSchemaDomainsAsync(dbName, schema);
                                if (domains != null) {
                                    foreach (var (dom, oid) in domains) {
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, dom, JunkType.Domain);
                                        await ProcessJunk(dbResult, dbName, schema, dom, null, JunkType.Domain, oid, null, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan domains in {schema}: {ex.Message}"); }
 
                            // Partitions
                            try {
                                var partitions = await _pgService.GetSchemaPartitionsAsync(dbName, schema);
                                if (partitions != null) {
                                    foreach (var (part, oid) in partitions) {
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, part, JunkType.Partition);
                                        await ProcessJunk(dbResult, dbName, schema, part, null, JunkType.Partition, oid, null, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan partitions in {schema}: {ex.Message}"); }
 
                            // Materialized Views
                            try {
                                var matViews = await _pgService.GetSchemaMatViewsAsync(dbName, schema);
                                if (matViews != null) {
                                    foreach (var (mv, oid) in matViews) {
                                        var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, mv, JunkType.MaterializedView);
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, mv, JunkType.MaterializedView);
                                        await ProcessJunk(dbResult, dbName, schema, mv, null, JunkType.MaterializedView, oid, def, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan matviews in {schema}: {ex.Message}"); }

                            // Sequences
                            try {
                                var sequences = await _pgService.GetSchemaSequencesAsync(dbName, schema);
                                if (sequences != null) {
                                    foreach (var (seq, oid) in sequences) {
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, seq, JunkType.Sequence);
                                        await ProcessJunk(dbResult, dbName, schema, seq, null, JunkType.Sequence, oid, null, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan sequences in {schema}: {ex.Message}"); }
 
                            // Aggregates
                            try {
                                var aggregates = await _pgService.GetSchemaAggregatesAsync(dbName, schema);
                                if (aggregates != null) {
                                    foreach (var (agg, oid) in aggregates) {
                                        var comment = await _pgService.GetObjectCommentAsync(dbName, schema, agg, JunkType.Aggregate);
                                        await ProcessJunk(dbResult, dbName, schema, agg, null, JunkType.Aggregate, oid, null, comment, keywords);
                                    }
                                }
                            } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan aggregates in {schema}: {ex.Message}"); }
                        }

                    if (selection.IncludeData)
                    {
                        try {
                            var tablePkSeen = new HashSet<string>();
                            foreach (var keyword in keywords)
                            {
                                var dataJunks = await _pgService.SearchJunkDataAsync(dbName, keyword, schema);
                                if (dataJunks == null) continue;
                                int count = 0;
                                foreach (var dj in dataJunks) {
                                    var pkKey = $"{dbName}.{schema}.{dj.TableName}.{dj.PrimaryKeyValue}";
                                    if (!tablePkSeen.Contains(pkKey)) {
                                        Console.WriteLine($"[DATA-JUNK] Creating JunkItem: Table={dj.TableName}, TableOid={dj.TableOid}, PkCol={dj.PrimaryKeyColumn}, PkValue={dj.PrimaryKeyValue}, Col={dj.ColumnName}");
                                        var mainItem = new JunkItem { 
                                            DatabaseName = dbName, 
                                            SchemaName = schema, 
                                            ObjectName = dj.TableName ?? "", 
                                            Type = JunkType.DataRecord, 
                                            ColumnName = dj.ColumnName, 
                                            PrimaryKeyColumn = dj.PrimaryKeyColumn, 
                                            PrimaryKeyValue = dj.PrimaryKeyValue, 
                                            Oid = dj.TableOid,
                                            DetectedContent = $"Found '{keyword}' in column '{dj.ColumnName}'", 
                                            RawData = dj.DetectedContent, 
                                            MatchedKeywords = keywords 
                                        };

                                        // Analyze Cascade Impact for Data Record
                                        Console.WriteLine($"[CASCADE-TRIGGER] Calling AnalyzeDataCascadeRecursiveAsync for {schema}.{dj.TableName} Oid={dj.TableOid} PK={dj.PrimaryKeyColumn}={dj.PrimaryKeyValue}");
                                        await AnalyzeDataCascadeRecursiveAsync(dbName, mainItem);

                                        dbResult.Items.Add(mainItem);
                                        tablePkSeen.Add(pkKey);
                                        if (++count > 50) break;
                                    }
                                }
                            }
                        } catch (Exception ex) { Console.WriteLine($"[ERROR] Failed to scan data records in {schema}: {ex.Message}"); }
                    }

                    // Roles scanning (Global to the database connection)
                    try {
                        var roles = await _pgService.GetRolesAsync(dbName);
                        if (roles != null) {
                            foreach (var (role, oid) in roles) {
                                var comment = await _pgService.GetObjectCommentAsync(dbName, "", role, JunkType.Role);
                                await ProcessJunk(dbResult, dbName, "", role, null, JunkType.Role, oid, null, comment, keywords);
                            }
                        }
                    } catch (Exception ex) { 
                        Console.WriteLine($"[ERROR] Failed to scan roles in {dbName}: {ex.Message}"); 
                        dbResult.Errors.Add($"Roles scan failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) { 
                Console.WriteLine($"Error analyzing DB {dbName}: {ex.Message}"); 
                dbResult.Errors.Add($"Database scan failed: {ex.Message}");
            }
            
            if (dbResult.Items.Any() || dbResult.Errors.Any()) results.Add(dbResult);
        }
        return results;
    }

    public string GenerateCleanupScript(List<JunkItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- Junk Cleanup Script Generated at " + DateTime.Now);
        if (!items.Any()) return sb.ToString() + "\n-- No items selected.";

        foreach (var dbGroup in items.GroupBy(i => i.DatabaseName))
        {
            sb.AppendLine($"\n-- DATABASE: {dbGroup.Key}");
            var junkSchemas = dbGroup.Where(i => i.Type == JunkType.Schema).Select(i => i.SchemaName).ToHashSet();
            foreach (var schema in junkSchemas) sb.AppendLine($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");

            foreach (var item in dbGroup.Where(i => !junkSchemas.Contains(i.SchemaName)))
            {
                switch (item.Type)
                {
                    case JunkType.Table: sb.AppendLine($"DROP TABLE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.View: sb.AppendLine($"DROP VIEW IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Routine: sb.AppendLine($"DROP ROUTINE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Index: sb.AppendLine($"DROP INDEX IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Column: sb.AppendLine($"ALTER TABLE \"{item.SchemaName}\".\"{item.ParentObjectName}\" DROP COLUMN IF EXISTS \"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Trigger: sb.AppendLine($"DROP TRIGGER IF EXISTS \"{item.ObjectName}\" ON \"{item.SchemaName}\".\"{item.ParentObjectName}\" CASCADE;"); break;
                    case JunkType.Constraint: sb.AppendLine($"ALTER TABLE \"{item.SchemaName}\".\"{item.ParentObjectName}\" DROP CONSTRAINT IF EXISTS \"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.DataType: sb.AppendLine($"DROP TYPE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Role: sb.AppendLine($"DROP ROLE IF EXISTS \"{item.ObjectName}\";"); break;
                    case JunkType.Partition: sb.AppendLine($"DROP TABLE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.MaterializedView: sb.AppendLine($"DROP MATERIALIZED VIEW IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Sequence: sb.AppendLine($"DROP SEQUENCE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Aggregate: sb.AppendLine($"DROP AGGREGATE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.Domain: sb.AppendLine($"DROP DOMAIN IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;"); break;
                    case JunkType.DataRecord: sb.AppendLine($"DELETE FROM \"{item.SchemaName}\".\"{item.ObjectName}\" WHERE \"{item.PrimaryKeyColumn}\" = '{item.PrimaryKeyValue}';"); break;
                }
            }
        }
        return sb.ToString();
    }

    private async Task ProcessJunk(JunkAnalysisResult dbResult, string dbName, string schema, string objName, string? tempParentName, JunkType type, uint oid, string? def, string? comment, List<string> keywords, List<string>? columnNames = null)
    {
        if (string.IsNullOrEmpty(objName)) return;
        
        // Exclude system/extension objects starting with pg_
        if (objName.StartsWith("pg_", StringComparison.OrdinalIgnoreCase)) return;

        Console.WriteLine($"[DEBUG] Checking '{type}': {schema}.{objName}");
        string matched;
        string reason = "";
        
        if (IsJunk(objName, keywords, out matched)) reason = $"Name contains junk: {matched}";
        else if (IsJunk(def, keywords, out matched)) reason = $"Source contains junk: {matched}";
        else if (IsJunk(comment, keywords, out matched)) reason = $"Comment contains junk: {matched}";
        else if (columnNames != null) {
            foreach (var col in columnNames) {
                if (IsJunk(col, keywords, out matched)) {
                    Console.WriteLine($"[SCAN-JUNK] Found junk column '{col}' in table {schema}.{objName}");
                    reason = $"Column '{col}' contains junk: {matched}";
                    break;
                }
            }
        }
        
        if (!string.IsNullOrEmpty(reason))
        {
            var rawData = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(tempParentName)) rawData.AppendLine($"Parent Table: {tempParentName}\n");
            if (!string.IsNullOrWhiteSpace(comment)) rawData.AppendLine($"Comment: {comment}\n");
            if (!string.IsNullOrWhiteSpace(def)) rawData.AppendLine(def);
            
            var mainItem = new JunkItem {
                DatabaseName = dbName, SchemaName = schema, Type = type, ObjectName = objName, ParentObjectName = tempParentName,
                Oid = oid, DetectedContent = reason, RawData = rawData.ToString().TrimEnd(), MatchedKeywords = keywords
            };
            
            // Analyze Cascade Impact
            if (oid > 0)
            {
                var dependents = await _pgService.GetDependentObjectsRecursiveAsync(dbName, oid);
                foreach (var dep in dependents)
                {
                    mainItem.DependentObjects.Add(new JunkItem
                    {
                        DatabaseName = dbName,
                        SchemaName = dep.Schema,
                        ObjectName = dep.Name,
                        Type = dep.Type,
                        Oid = dep.Oid,
                        IsCascadeImpact = true,
                        DetectedContent = $"Cascade Impact: Depends on {type} '{objName}'",
                        MatchedKeywords = keywords // Pass along keywords for highlighting if needed
                    });
                }
            }
            
            dbResult.Items.Add(mainItem);
        }
    }

    private async Task AnalyzeDataCascadeRecursiveAsync(string dbName, JunkItem parentItem, int currentDepth = 0, int maxDepth = 3)
    {
        if (currentDepth >= maxDepth) return;
        if (string.IsNullOrEmpty(parentItem.PrimaryKeyColumn) || string.IsNullOrEmpty(parentItem.PrimaryKeyValue)) return;

        Console.WriteLine($"[CASCADE-RECURSIVE] depth={currentDepth}, table={parentItem.ObjectName}, oid={parentItem.Oid}, pk={parentItem.PrimaryKeyColumn}={parentItem.PrimaryKeyValue}");
        try
        {
            var impacts = await _pgService.GetFkCascadeImpactAsync(dbName, parentItem.Oid, parentItem.PrimaryKeyColumn!, parentItem.PrimaryKeyValue!);
            
            foreach (var (childSchema, childTable, fkCol, count) in impacts)
            {
                var childPks = await _pgService.GetPrimaryKeysAsync(dbName, childTable, childSchema);
                string childPkCol = childPks.FirstOrDefault() ?? "";

                var depItem = new JunkItem
                {
                    DatabaseName = dbName,
                    SchemaName = childSchema,
                    ObjectName = childTable,
                    Type = JunkType.DataRecord,
                    IsCascadeImpact = true,
                    PrimaryKeyColumn = childPkCol,
                    DetectedContent = $"Cascade Impact: {count} rows in {childSchema}.{childTable} reference this record via {fkCol}",
                    MatchedKeywords = parentItem.MatchedKeywords,
                    FkColumn = fkCol,
                    FkValue = parentItem.PrimaryKeyValue
                };

                parentItem.DependentObjects.Add(depItem);

                // For deep recursion, we'd need to fetch actual PK values of child rows.
                // To avoid performance issues during scan, we only do one level of detailed row check OR 
                // we only recurse if the count is small enough to not lock up the UI.
                if (count > 0 && count <= 500 && !string.IsNullOrEmpty(childPkCol))
                {
                    var childPkValues = await _pgService.GetFkChildPkValuesAsync(dbName, childSchema, childTable, childPkCol, fkCol, parentItem.PrimaryKeyValue);
                    foreach (var childPkVal in childPkValues)
                    {
                        var rowItem = new JunkItem
                        {
                            DatabaseName = dbName,
                            SchemaName = childSchema,
                            ObjectName = childTable,
                            Type = JunkType.DataRecord,
                            IsCascadeImpact = true,
                            PrimaryKeyColumn = childPkCol,
                            PrimaryKeyValue = childPkVal,
                            DetectedContent = $"[ROW DETAIL] {childPkCol}={childPkVal} references parent",
                            MatchedKeywords = parentItem.MatchedKeywords
                        };
                        
                        await AnalyzeDataCascadeRecursiveAsync(dbName, rowItem, currentDepth + 1, maxDepth);
                        // Add the row detail directly under the group node!
                        depItem.DependentObjects.Add(rowItem);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CASCADE-ERROR] Recursion failed for {parentItem.ObjectName}: {ex.Message}");
        }
    }

    private bool IsJunk(string? text, List<string> keywords, out string matchedKeyword)
    {
        matchedKeyword = "";
        if (string.IsNullOrEmpty(text)) return false;
        
        foreach (var kw in keywords) {
            string trimmedKw = kw.Trim();
            if (string.IsNullOrEmpty(trimmedKw)) continue;

            // Pattern: Match if the keyword is preceded by start of line or non-alphanumeric char (including underscore) 
            // and followed by non-alphanumeric or end of line.
            // Simplified for tech names: keyword should be a "token" separated by . _ - or spaces.
            // We use Regex with CaseInsensitive. 
            // Custom boundary: (^|[^a-zA-Z0-9])keyword([^a-zA-Z0-9]|$)
            string pattern = $@"(?i)(^|[^a-zA-Z0-9]){Regex.Escape(trimmedKw)}([^a-zA-Z0-9]|$)";
            
            if (Regex.IsMatch(text, pattern)) {
                matchedKeyword = trimmedKw;
                return true;
            }
        }
        return false;
    }
}
