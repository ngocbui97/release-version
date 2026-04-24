using NUnit.Framework;
using ReleasePrepTool.Services;
using ReleasePrepTool.Models;
using System.Collections.Generic;
using System.Linq;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class DatabaseCompareServiceTests
    {
        private DatabaseCompareService _service = default!;

        [SetUp]
        public void SetUp()
        {
            var source = new DatabaseConfig { DatabaseName = "src" };
            var target = new DatabaseConfig { DatabaseName = "tgt" };
            _service = new DatabaseCompareService(source, target);
        }

        [Test]
        public void MapPostgresType_ShouldReturnCorrectSimplifiedType()
        {
            Assert.That(_service.MapPostgresType("integer"), Is.EqualTo("int4"));
            Assert.That(_service.MapPostgresType("bigint"), Is.EqualTo("int8"));
            Assert.That(_service.MapPostgresType("boolean"), Is.EqualTo("bool"));
            Assert.That(_service.MapPostgresType("character varying"), Is.EqualTo("varchar"));
            Assert.That(_service.MapPostgresType("text"), Is.EqualTo("text"));
        }

        [Test]
        public void SortTablesTopologically_ShouldRespectDependencies()
        {
            // Arrange
            var tables = new List<string> { "child", "parent", "grandparent" };
            var deps = new List<(string Table, string DependsOn)> {
                ("child", "parent"),
                ("parent", "grandparent")
            };

            // Act
            var sorted = _service.SortTablesTopologically(tables, deps);

            // Assert
            // Parent-first order for inserts: grandparent -> parent -> child
            Assert.That(sorted[0], Is.EqualTo("grandparent"));
            Assert.That(sorted[1], Is.EqualTo("parent"));
            Assert.That(sorted[2], Is.EqualTo("child"));
        }

        [Test]
        public void SortTablesTopologically_WithNoDeps_ShouldKeepOrder()
        {
            var tables = new List<string> { "t1", "t2", "t3" };
            var deps = new List<(string Table, string DependsOn)>();

            var sorted = _service.SortTablesTopologically(tables, deps);

            Assert.That(sorted, Is.EquivalentTo(tables));
        }

        [Test]
        public void SortTablesTopologically_WithCircularDep_ShouldHandleGracefully()
        {
            var tables = new List<string> { "a", "b" };
            var deps = new List<(string Table, string DependsOn)> {
                ("a", "b"),
                ("b", "a")
            };

            // Should not throw and should contain both tables
            var sorted = _service.SortTablesTopologically(tables, deps);
            Assert.That(sorted.Count, Is.EqualTo(2));
        }
    }
}
