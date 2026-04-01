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

    public async Task<List<JunkAnalysisResult>> AnalyzeAsync(string[] databases, List<string> keywords)
    {
        var results = new List<JunkAnalysisResult>();

        foreach (var dbName in databases)
        {
            var dbResult = new JunkAnalysisResult { DatabaseName = dbName };
            
            try
            {
                // 1. Analyze Schemas
                var schemas = await _pgService.GetSchemasAsync(dbName);
                foreach (var schema in schemas)
                {
                    if (IsJunk(schema, keywords))
                    {
                        dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Schema, ObjectName = schema, DetectedContent = $"Schema name contains keyword" });
                        continue; // If schema is junk, no need to scan its content (it will be dropped anyway)
                    }

                    // 2. Analyze Objects (Tables, Views, Routines) in each non-junk schema
                    var tables = await _pgService.GetSchemaTablesAsync(dbName, schema);
                    foreach (var table in tables)
                    {
                        if (IsJunk(table, keywords))
                        {
                            dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Table, ObjectName = table, DetectedContent = $"Table name contains keyword" });
                        }
                    }

                    var views = await _pgService.GetSchemaViewsAsync(dbName, schema);
                    foreach (var view in views)
                    {
                        if (IsJunk(view, keywords))
                        {
                            dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.View, ObjectName = view, DetectedContent = $"View name contains keyword" });
                        }
                    }

                    var routines = await _pgService.GetSchemaRoutinesAsync(dbName, schema);
                    foreach (var routine in routines)
                    {
                        if (IsJunk(routine, keywords))
                        {
                            dbResult.Items.Add(new JunkItem { DatabaseName = dbName, SchemaName = schema, Type = JunkType.Routine, ObjectName = routine, DetectedContent = $"Routine name contains keyword" });
                        }
                    }

                    // 3. Analyze Data Records (only in tables not already flagged as junk)
                    // We prevent duplicates by checking Table + PK combination
                    var tablePkSeen = new HashSet<string>();
                    
                    foreach (var keyword in keywords)
                    {
                         var dataJunks = await _pgService.SearchJunkDataAsync(keyword, schema);
                         // Limit to top 5 samples per schema to avoid Tree bloat
                         int count = 0;
                         foreach (var dj in dataJunks)
                         {
                             var pkKey = $"{dbName}.{schema}.{dj.TableName}.{dj.PrimaryKeyValue}";
                             if (!tablePkSeen.Contains(pkKey))
                             {
                                 dbResult.Items.Add(new JunkItem
                                 {
                                     DatabaseName = dbName,
                                     SchemaName = schema,
                                     ObjectName = dj.TableName ?? "",
                                     Type = JunkType.DataRecord,
                                     ColumnName = dj.ColumnName,
                                     PrimaryKeyColumn = dj.PrimaryKeyColumn,
                                     PrimaryKeyValue = dj.PrimaryKeyValue,
                                     DetectedContent = $"Found '{keyword}' in row with PK: {dj.PrimaryKeyValue}"
                                 });
                                 tablePkSeen.Add(pkKey);
                                 count++;
                                 if (count > 50) break; // Limit total records shown per schema (e.g. 50 samples)
                             }
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing DB {dbName}: {ex.Message}");
            }

            if (dbResult.Items.Any())
                results.Add(dbResult);
        }

        return results;
    }

    public string GenerateCleanupScript(List<JunkItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- Junk Cleanup Script Generated at " + DateTime.Now);
        
        if (!items.Any()) return sb.ToString() + "\n-- No items selected.";

        var groupedByDb = items.GroupBy(i => i.DatabaseName);
        foreach (var dbGroup in groupedByDb)
        {
            sb.AppendLine($"\n-- DATABASE: {dbGroup.Key}");
            
            // Drop Schemas first (CASCADE)
            var junkSchemas = dbGroup.Where(i => i.Type == JunkType.Schema).Select(i => i.SchemaName).ToHashSet();
            foreach (var schema in junkSchemas)
                sb.AppendLine($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");

            // Drop Objects next (Tables/Views/Routines)
            foreach (var item in dbGroup.Where(i => !junkSchemas.Contains(i.SchemaName)))
            {
                switch (item.Type)
                {
                    case JunkType.Table:
                        sb.AppendLine($"DROP TABLE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;");
                        break;
                    case JunkType.View:
                        sb.AppendLine($"DROP VIEW IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;");
                        break;
                    case JunkType.Routine:
                        sb.AppendLine($"DROP ROUTINE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;");
                        break;
                    case JunkType.DataRecord:
                        sb.AppendLine($"DELETE FROM \"{item.SchemaName}\".\"{item.ObjectName}\" WHERE \"{item.PrimaryKeyColumn}\" = '{item.PrimaryKeyValue}';");
                        break;
                }
            }
        }
        
        return sb.ToString();
    }

    private bool IsJunk(string name, List<string> keywords)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return keywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }
}
