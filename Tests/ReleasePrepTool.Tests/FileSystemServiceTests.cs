using NUnit.Framework;
using ReleasePrepTool.Services;
using System;
using System.IO;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class FileSystemServiceTests
    {
        private string _tempPath = default!;

        [SetUp]
        public void SetUp()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "ReleasePrepToolTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempPath))
                Directory.Delete(_tempPath, true);
        }

        [Test]
        public void Constructor_ShouldSetBaseReleasePathCorrectly()
        {
            var service = new FileSystemService(_tempPath, "1.2.3", "MyProduct");
            var expected = Path.Combine(_tempPath, "MyProduct_version_1.2.3");
            Assert.That(service.BaseReleasePath, Is.EqualTo(expected));
        }

        [Test]
        public void Constructor_WithEmptyProduct_ShouldUseDefaultProductName()
        {
            var service = new FileSystemService(_tempPath, "1.2.3", "");
            var expected = Path.Combine(_tempPath, "product_version_1.2.3");
            Assert.That(service.BaseReleasePath, Is.EqualTo(expected));
        }

        [Test]
        public void GetSqlScriptPath_ShouldReturnCorrectPath()
        {
            var service = new FileSystemService(_tempPath, "1.0", "test");
            var path = service.GetSqlScriptPath("mydb", true);
            Assert.That(path, Does.Contain("script_update"));
            Assert.That(path, Does.Contain("schema"));
            Assert.That(path, Does.EndWith("mydb_schema.sql"));
        }

        [Test]
        public void GetBackupPath_ShouldReturnCorrectPath()
        {
            var service = new FileSystemService(_tempPath, "1.0", "test");
            var path = service.GetBackupPath("mydb");
            Assert.That(path, Does.EndWith("mydb.backup"));
            Assert.That(path, Does.Contain("backup"));
        }

        [Test]
        public void EnsureDirectoryStructure_ShouldCreateNeededFolders()
        {
            var service = new FileSystemService(_tempPath, "2.0", "app");
            service.EnsureDirectoryStructure();
            
            Assert.That(Directory.Exists(service.BaseReleasePath), Is.True);
            Assert.That(Directory.Exists(Path.Combine(service.BaseReleasePath, "database", "backup")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(service.BaseReleasePath, "database", "script_update", "schema")), Is.True);
        }
    }
}
