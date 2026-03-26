using System;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class CompareDataTests
    {
        private Application _app;
        private UIA3Automation _automation;
        private string _appPath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Path to the executable
            _appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "bin", "Debug", "net10.0-windows", "ReleasePrepTool.exe");
            _appPath = Path.GetFullPath(_appPath);

            if (!File.Exists(_appPath))
            {
                throw new FileNotFoundException($"Executable not found at {_appPath}");
            }

            _automation = new UIA3Automation();
            _app = Application.Launch(_appPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _app?.Close();
            _automation?.Dispose();
        }

        [Test]
        public void VerifyCompareDataTabIsUnique()
        {
            var window = _app.GetMainWindow(_automation);
            Assert.That(window, Is.Not.Null, "Main window should be found");

            var tabControl = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tab)).AsTab();
            Assert.That(tabControl, Is.Not.Null, "Tab control should be found");

            var compareDataTabs = tabControl.TabItems.Where(t => t.Name == "4. Compare Data").ToList();
            
            Assert.That(compareDataTabs.Count, Is.EqualTo(1), $"Expected 1 '4. Compare Data' tab, but found {compareDataTabs.Count}.");
        }

        [Test]
        public void TestCompareDataFunctionalFlow()
        {
            var window = _app.GetMainWindow(_automation);
            var tabControl = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tab)).AsTab();
            var compareDataTab = tabControl.TabItems.FirstOrDefault(t => t.Name == "4. Compare Data");
            compareDataTab?.Select();

            // 1. Check for DB selection ComboBoxes
            var sourceCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("cmbSourceDataDb"))?.AsComboBox();
            var targetCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("cmbTargetDataDb"))?.AsComboBox();

            Assert.That(sourceCombo, Is.Not.Null, "Source ComboBox should exist");
            Assert.That(targetCombo, Is.Not.Null, "Target ComboBox should exist");

            // 2. Find Buttons
            var btnCompare = window.FindFirstDescendant(cf => cf.ByAutomationId("btnCompareData"))?.AsButton();
            Assert.That(btnCompare, Is.Not.Null, "'Start Comparison' button should exist");

            // 3. Find Grid
            var grid = window.FindFirstDescendant(cf => cf.ByAutomationId("dgvTableDiffs"))?.AsDataGridView();
            Assert.That(grid, Is.Not.Null, "Table Diff Grid should exist");

            // 4. (Optional) Run Load Tables - This might fail if DBs aren't initialized, but we can try
            // btnLoad.Invoke();
            // grid.Rows.Count should be > 0 if successful
        }

        [Test]
        public void VerifyGridColumns()
        {
            var window = _app.GetMainWindow(_automation);
            var tabControl = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tab)).AsTab();
            tabControl.TabItems.FirstOrDefault(t => t.Name == "4. Compare Data")?.Select();

            var grid = window.FindFirstDescendant(cf => cf.ByAutomationId("dgvTableDiffs"))?.AsDataGridView();
            Assert.That(grid, Is.Not.Null, "DataGrid should be present");

            // Verify expected columns (names based on MainForm.cs:269-274)
            // Note: FlaUI might see Header names or AutomationIds
            var columnHeaders = grid.Header.Columns.Select(c => c.Name).ToList();
            Assert.That(columnHeaders, Contains.Item("Table Name"), "Grid should have 'Table Name' column");
            Assert.That(columnHeaders, Contains.Item("Different"), "Grid should have 'Different' column");
            Assert.That(columnHeaders, Contains.Item("Identical"), "Grid should have 'Identical' column");
        }
    }
}
