using Moq;
using NUnit.Framework;
using ReleasePrepTool.Services;
using ReleasePrepTool.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class JunkAnalysisServiceTests
    {
        private Mock<PostgresService> _mockPgService = default!;
        private JunkAnalysisService _service = default!;

        [SetUp]
        public void SetUp()
        {
            var dummyConfig = new DatabaseConfig { Host = "localhost", Username = "user", Password = "pwd" };
            _mockPgService = new Mock<PostgresService>(dummyConfig);
            _service = new JunkAnalysisService(_mockPgService.Object);
            
            _mockPgService.Setup(s => s.GetSchemasAsync(It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetRolesAsync(It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaTablesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaViewsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaColumnsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(uint, string, string)>());
            _mockPgService.Setup(s => s.GetSchemaRoutinesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaIndexesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaTriggersAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaConstraintsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaTypesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaDomainsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaPartitionsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaMatViewsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaSequencesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.GetSchemaAggregatesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<(string, uint)>());
            _mockPgService.Setup(s => s.SearchJunkDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<PostgresService.JunkRecord>());
            _mockPgService.Setup(s => s.GetObjectCommentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JunkType>())).ReturnsAsync("");
            _mockPgService.Setup(s => s.GetObjectDefinitionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JunkType>(), It.IsAny<uint>())).ReturnsAsync("");
            _mockPgService.Setup(s => s.GetDependentObjectsRecursiveAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<int>())).ReturnsAsync(new List<(string, string, JunkType, uint)>());
        }

        [Test]
        public async Task AnalyzeAsync_ShouldNotMatchKeywordsInsideTechnicalWords()
        {
            var dbName = "testdb";
            var keywords = new List<string> { "dev" };
            _mockPgService.Setup(s => s.GetSchemasAsync(dbName)).ReturnsAsync(new List<(string Name, uint Oid)> { ("public", 1) });
            _mockPgService.Setup(s => s.GetSchemaTablesAsync(dbName, "public")).ReturnsAsync(new List<(string Name, uint Oid)> { 
                ("dev_table", 100), 
                ("stddev_func_table", 101) 
            });

            var result = await _service.AnalyzeAsync(new[] { dbName }, keywords);
            var items = result[0].Items;

            Assert.That(items.Any(i => i.ObjectName == "dev_table"), Is.True, "Should match dev_table");
            Assert.That(items.Any(i => i.ObjectName == "stddev_func_table"), Is.False, "Should NOT match stddev_func_table");
        }

        [Test]
        public async Task AnalyzeAsync_ShouldSkipSystemObjectsStartingWithPg()
        {
            var dbName = "testdb";
            var keywords = new List<string> { "stat" };
            _mockPgService.Setup(s => s.GetSchemasAsync(dbName)).ReturnsAsync(new List<(string Name, uint Oid)> { ("public", 1) });
            _mockPgService.Setup(s => s.GetSchemaViewsAsync(dbName, "public")).ReturnsAsync(new List<(string Name, uint Oid)> { 
                ("pg_stat_statements", 200), // Matches 'stat' but starts with 'pg_'
                ("stat_collector", 201)      // Matches 'stat' and doesn't start with 'pg_'
            });

            var result = await _service.AnalyzeAsync(new[] { dbName }, keywords);
            var items = result[0].Items;

            Assert.That(items.Any(i => i.ObjectName == "pg_stat_statements"), Is.False, "Should skip pg_ objects");
            Assert.That(items.Any(i => i.ObjectName == "stat_collector"), Is.True, "Should include non-pg objects that match keyword");
        }

        [Test]
        public async Task AnalyzeAsync_ShouldCorrectlyMapColumnsByOid()
        {
            var dbName = "testdb";
            var keywords = new List<string> { "junk" };
            _mockPgService.Setup(s => s.GetSchemasAsync(dbName)).ReturnsAsync(new List<(string Name, uint Oid)> { ("public", 1) });
            _mockPgService.Setup(s => s.GetSchemaTablesAsync(dbName, "public")).ReturnsAsync(new List<(string Name, uint Oid)> { 
                ("TableA", 100), 
                ("tablea", 101) 
            });

            _mockPgService.Setup(s => s.GetSchemaColumnsAsync(dbName, "public")).ReturnsAsync(new List<(uint TableOid, string TableName, string Column)> { 
                (101, "tablea", "junk_col"),
                (100, "TableA", "normal_col")
            });

            var result = await _service.AnalyzeAsync(new[] { dbName }, keywords);
            var items = result[0].Items;

            var item101 = items.FirstOrDefault(i => i.Oid == 101);
            var item100 = items.FirstOrDefault(i => i.Oid == 100);

            Assert.That(item101, Is.Not.Null);
            Assert.That(item101!.DetectedContent, Does.Contain("Column 'junk_col'"), "Table 101 should be junk");
            Assert.That(item100, Is.Null, "Table 100 should NOT be junk even if name is similar");
        }

        [Test]
        public async Task AnalyzeAsync_ShouldDetectJunkInRoleName()
        {
            var dbName = "testdb";
            var keywords = new List<string> { "temp" };
            _mockPgService.Setup(s => s.GetSchemasAsync(dbName)).ReturnsAsync(new List<(string Name, uint Oid)> { ("public", 1) });
            _mockPgService.Setup(s => s.GetRolesAsync(dbName)).ReturnsAsync(new List<(string Name, uint Oid)> { ("temp_user", 100), ("real_user", 101) });

            var results = await _service.AnalyzeAsync(new[] { dbName }, keywords);

            Assert.That(results.Count, Is.GreaterThan(0));
            Assert.That(results[0].Items.Any(i => i.ObjectName == "temp_user"), Is.True);
        }

        [Test]
        public void GenerateCleanupScript_ShouldGenerateCorrectDropStatement()
        {
            var items = new List<JunkItem> {
                new JunkItem { DatabaseName = "db1", SchemaName = "public", Type = JunkType.Table, ObjectName = "old_table" },
                new JunkItem { DatabaseName = "db1", SchemaName = "public", Type = JunkType.View, ObjectName = "old_view" }
            };

            var script = _service.GenerateCleanupScript(items);

            Assert.That(script, Does.Contain("DROP TABLE IF EXISTS \"public\".\"old_table\" CASCADE;"));
            Assert.That(script, Does.Contain("DROP VIEW IF EXISTS \"public\".\"old_view\" CASCADE;"));
        }
    }
}
