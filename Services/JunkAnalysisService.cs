using ReleasePrepTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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
        var selectionDict = schemaSelections?.ToDictionary(s => s.SchemaName, s => s) ?? new Dictionary<string, SchemaSelection>();

        foreach (var dbName in databases)
        {
            var dbResult = new JunkAnalysisResult { DatabaseName = dbName };
            try
            {
                var schemas = await _pgService.GetSchemasAsync(dbName);
                
                // Roles (Global per DB/Server)
                var roles = await _pgService.GetRolesAsync(dbName);
                foreach (var role in roles) {
                    var comment = await _pgService.GetObjectCommentAsync(dbName, "", role, JunkType.Role);
                    if (IsJunk(role, keywords, out var matched) || IsJunk(comment, keywords, out matched)) {
                        dbResult.Items.Add(new JunkItem { DatabaseName = dbName, Type = JunkType.Role, ObjectName = role, DetectedContent = $"Role (name/comment) contains junk: {matched}", RawData = $"Role: {role}\nComment: {comment}", MatchedKeywords = keywords });
                    }
                }

                foreach (var schema in schemas)
                {
                    if (schemaSelections != null && !selectionDict.ContainsKey(schema)) continue;
                    var selection = selectionDict.GetValueOrDefault(schema) ?? new SchemaSelection { SchemaName = schema };

                    var schemaComment = await _pgService.GetObjectCommentAsync(dbName, schema, schema, JunkType.Schema);
                    if (IsJunk(schema, keywords, out var schemaMatched) || IsJunk(schemaComment, keywords, out schemaMatched))
                    {
                        if (selection.IncludeStructure)
                            dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Schema, ObjectName = schema, DetectedContent = $"Schema (name/comment) contains junk: {schemaMatched}", RawData = $"Schema: {schema}\nComment: {schemaComment}", MatchedKeywords = keywords });
                        continue;
                    }

                    if (selection.IncludeStructure)
                    {
                        // Tables
                        var tables = await _pgService.GetSchemaTablesAsync(dbName, schema);
                        foreach (var table in tables) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, table, JunkType.Table);
                            if (IsJunk(table, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Table, ObjectName = table, DetectedContent = $"Table (name/comment) contains junk: {matched}", RawData = $"Table: {table}\nComment: {comment}", MatchedKeywords = keywords });
                        }

                        // Views
                        var views = await _pgService.GetSchemaViewsAsync(dbName, schema);
                        foreach (var view in views) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, view, JunkType.View);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, view, JunkType.View);
                            if (IsJunk(view, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.View, ObjectName = view, DetectedContent = $"View (name/source/comment) contains junk: {matched}", RawData = $"Comment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Routines
                        var routines = await _pgService.GetSchemaRoutinesAsync(dbName, schema);
                        foreach (var routine in routines) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, routine, JunkType.Routine);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, routine, JunkType.Routine);
                            if (IsJunk(routine, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Routine, ObjectName = routine, DetectedContent = $"Routine (name/source/comment) contains junk: {matched}", RawData = $"Comment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Indexes
                        var indexes = await _pgService.GetSchemaIndexesAsync(dbName, schema);
                        foreach (var idx in indexes) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, idx, JunkType.Index);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, idx, JunkType.Index);
                            if (IsJunk(idx, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Index, ObjectName = idx, DetectedContent = $"Index (name/source/comment) contains junk: {matched}", RawData = $"Comment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Triggers
                        var triggers = await _pgService.GetSchemaTriggersAsync(dbName, schema);
                        foreach (var trg in triggers) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, trg.Trigger, JunkType.Trigger);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, trg.Trigger, JunkType.Trigger);
                            if (IsJunk(trg.Trigger, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Trigger, ObjectName = trg.Trigger, ParentObjectName = trg.Table, DetectedContent = $"Trigger (name/source/comment) contains junk: {matched}", RawData = $"Table: {trg.Table}\nComment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Constraints
                        var constraints = await _pgService.GetSchemaConstraintsAsync(dbName, schema);
                        foreach (var con in constraints) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, con.Constraint, JunkType.Constraint);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, con.Constraint, JunkType.Constraint);
                            if (IsJunk(con.Constraint, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Constraint, ObjectName = con.Constraint, ParentObjectName = con.Table, DetectedContent = $"Constraint (name/source/comment) contains junk: {matched}", RawData = $"Table: {con.Table}\nComment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Data Types
                        var types = await _pgService.GetSchemaTypesAsync(dbName, schema);
                        foreach (var typ in types) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, typ, JunkType.DataType);
                            if (IsJunk(typ, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.DataType, ObjectName = typ, DetectedContent = $"Type (name/comment) contains junk: {matched}", RawData = $"Type: {typ}\nComment: {comment}", MatchedKeywords = keywords });
                        }

                        // Domains
                        var domains = await _pgService.GetSchemaDomainsAsync(dbName, schema);
                        foreach (var dom in domains) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, dom, JunkType.Domain);
                            if (IsJunk(dom, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Domain, ObjectName = dom, DetectedContent = $"Domain (name/comment) contains junk: {matched}", RawData = $"Domain: {dom}\nComment: {comment}", MatchedKeywords = keywords });
                        }

                        // Partitions
                        var partitions = await _pgService.GetSchemaPartitionsAsync(dbName, schema);
                        foreach (var part in partitions) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, part, JunkType.Partition);
                            if (IsJunk(part, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Partition, ObjectName = part, DetectedContent = $"Partition (name/comment) contains junk: {matched}", RawData = $"Partition: {part}\nComment: {comment}", MatchedKeywords = keywords });
                        }

                        // Materialized Views
                        var matViews = await _pgService.GetSchemaMatViewsAsync(dbName, schema);
                        foreach (var mv in matViews) {
                            var def = await _pgService.GetObjectDefinitionAsync(dbName, schema, mv, JunkType.MaterializedView);
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, mv, JunkType.MaterializedView);
                            if (IsJunk(mv, keywords, out var matched) || IsJunk(def, keywords, out matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.MaterializedView, ObjectName = mv, DetectedContent = $"MatView (name/source/comment) contains junk: {matched}", RawData = $"Comment: {comment}\n\n{def}", MatchedKeywords = keywords });
                        }

                        // Sequences
                        var sequences = await _pgService.GetSchemaSequencesAsync(dbName, schema);
                        foreach (var seq in sequences) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, seq, JunkType.Sequence);
                            if (IsJunk(seq, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Sequence, ObjectName = seq, DetectedContent = $"Sequence (name/comment) contains junk: {matched}", RawData = $"Sequence: {seq}\nComment: {comment}", MatchedKeywords = keywords });
                        }

                        // Aggregates
                        var aggregates = await _pgService.GetSchemaAggregatesAsync(dbName, schema);
                        foreach (var agg in aggregates) {
                            var comment = await _pgService.GetObjectCommentAsync(dbName, schema, agg, JunkType.Aggregate);
                            if (IsJunk(agg, keywords, out var matched) || IsJunk(comment, keywords, out matched))
                                dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Aggregate, ObjectName = agg, DetectedContent = $"Aggregate (name/comment) contains junk: {matched}", RawData = $"Aggregate: {agg}\nComment: {comment}", MatchedKeywords = keywords });
                        }
                    }

                    if (selection.IncludeData)
                    {
                        var tablePkSeen = new HashSet<string>();
                        foreach (var keyword in keywords)
                        {
                            var dataJunks = await _pgService.SearchJunkDataAsync(keyword, schema);
                            int count = 0;
                            foreach (var dj in dataJunks) {
                                var pkKey = $"{dbName}.{schema}.{dj.TableName}.{dj.PrimaryKeyValue}";
                                if (!tablePkSeen.Contains(pkKey)) {
                                    dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, ObjectName = dj.TableName ?? "", Type = JunkType.DataRecord, ColumnName = dj.ColumnName, PrimaryKeyColumn = dj.PrimaryKeyColumn, PrimaryKeyValue = dj.PrimaryKeyValue, DetectedContent = $"Found '{keyword}' in column '{dj.ColumnName}'", RawData = dj.DetectedContent, MatchedKeywords = keywords });
                                    tablePkSeen.Add(pkKey);
                                    if (++count > 50) break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error analyzing DB {dbName}: {ex.Message}"); }
            if (dbResult.Items.Any()) results.Add(dbResult);
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

    private bool IsJunk(string name, List<string> keywords, out string matchedKeyword)
    {
        matchedKeyword = "";
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var kw in keywords) {
            if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) {
                matchedKeyword = kw;
                return true;
            }
        }
        return false;
    }
}
