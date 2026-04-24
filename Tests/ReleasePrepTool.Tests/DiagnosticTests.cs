using NUnit.Framework;
using ReleasePrepTool.Services;
using ReleasePrepTool.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class DiagnosticTests
    {
        [Test]
        public async Task RunRealDiagnostic()
        {
            // Use the real NewConfig from appsettings.local.json
            var dbConfig = new DatabaseConfig {
                Host = "172.23.90.236",
                Port = 5432,
                Username = "postgres",
                Password = "B@sebs1234",
                DatabaseName = "U01"
            };

            var pgService = new PostgresService(dbConfig);
            var junkService = new JunkAnalysisService(pgService);
            var keywords = new List<string> { "test", "dev", "tmp", "123" };

            Console.WriteLine($"[DIAG] Connecting to {dbConfig.Host} for database U01...");
            
            // 1. Get OID for U01_User
            var tables = await pgService.GetSchemaTablesAsync("U01", "public");
            var targetTable = tables.FirstOrDefault(t => t.Name == "U01_User");
            
            if (targetTable == default) {
                Console.WriteLine("[DIAG] ERROR: Table U01_User not found in public schema.");
                return;
            }
            
            Console.WriteLine($"[DIAG] Found U01_User Table OID: {targetTable.Oid}");

            // 2. Scan columns using the same logic as AnalyzeAsync
            var columns = await pgService.GetSchemaColumnsAsync("U01", "public");
            var userColumns = columns.Where(c => c.TableOid == targetTable.Oid).Select(c => c.Column).ToList();
            
            Console.WriteLine($"[DIAG] Columns found during scan for U01_User: {string.Join(", ", userColumns)}");

            // 3. Get DDL using GetObjectDefinitionAsync
            Console.WriteLine("[DIAG] Fetching DDL via GetObjectDefinitionAsync...");
            var ddl = await pgService.GetObjectDefinitionAsync("U01", "public", "U01_User", JunkType.Table, targetTable.Oid);
            
            Console.WriteLine("\n=== DDL OUTPUT ===");
            Console.WriteLine(ddl);
            Console.WriteLine("==================");
        }
    }
}
