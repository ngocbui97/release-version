using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;

namespace ReleasePrepTool.Tests
{
    [TestFixture]
    public class EndToEndTests
    {
        private Application _app;
        private UIA3Automation _automation;
        private string _appPath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "bin", "Debug", "net10.0-windows", "ReleasePrepTool.exe");
            _appPath = Path.GetFullPath(_appPath);

            if (!File.Exists(_appPath))
            {
                throw new FileNotFoundException($"Executable not found at {_appPath}");
            }

            _automation = new UIA3Automation();
            _app = Application.Launch(_appPath);
            Thread.Sleep(2000); // Wait for app to start
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _app?.Close();
            _automation?.Dispose();
        }

        [Test]
        public void ExecuteEndToEndFlowWithoutScripts()
        {
            var window = _app.GetMainWindow(_automation);
            Assert.That(window, Is.Not.Null, "Main window should be found");

            var tabControl = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tab)).AsTab();
            Assert.That(tabControl, Is.Not.Null, "Tab control should be found");

            // --- 1. CONFIG & CONNECT ---
            var configTab = tabControl.TabItems.FirstOrDefault(t => t.Name == "1. Config");
            configTab?.Select();
            Thread.Sleep(500);

            var buttons = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
            var btnConnect = buttons.FirstOrDefault(b => b.Name.Contains("Connect"))?.AsButton();
            Assert.That(btnConnect, Is.Not.Null, "'Initialize / Connect' button should exist");
            btnConnect.Click();
            Thread.Sleep(2000); // Wait for connection

            // Dismiss the success Message Box (auto-initialize success)
            var msgBox = window.FindFirstDescendant(cf => cf.ByName("OK"))?.AsButton();
            if (msgBox != null) msgBox.Click();

            // --- 3. COMPARE SCHEMA ---
            var schemaTab = tabControl.TabItems.FirstOrDefault(t => t.Name == "3. Compare Schema");
            schemaTab?.Select();
            Thread.Sleep(500);

            buttons = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
            var btnLoadDiffs = buttons.FirstOrDefault(b => b.Name.Contains("Load Diffs"))?.AsButton();
            Assert.That(btnLoadDiffs, Is.Not.Null, "'Load Diffs' button should exist");
            btnLoadDiffs.Click();
            
            // Wait for diffs to load (could take a few seconds)
            Thread.Sleep(5000);

            // --- 4. COMPARE DATA ---
            var dataTab = tabControl.TabItems.FirstOrDefault(t => t.Name == "4. Compare Data");
            dataTab?.Select();
            Thread.Sleep(500);

            var btnCompareData = window.FindFirstDescendant(cf => cf.ByName("Start Comparison"))?.AsButton();
            Assert.That(btnCompareData, Is.Not.Null, "'Start Comparison' button should exist");
            btnCompareData.Click();

            // Wait for data comparison to finish
            Thread.Sleep(5000);

            // --- 6. CLEAN JUNK ---
            var junkTab = tabControl.TabItems.FirstOrDefault(t => t.Name == "6. Clean Junk");
            junkTab?.Select();
            Thread.Sleep(500);

            var btnScanJunk = window.FindFirstDescendant(cf => cf.ByName("Scan Junk Data"))?.AsButton();
            Assert.That(btnScanJunk, Is.Not.Null, "'Scan Junk Data' button should exist");
            btnScanJunk.Click();

            Thread.Sleep(3000);
            
            // Dismiss the junk found Message Box
            var junkMsgBox = window.FindFirstDescendant(cf => cf.ByName("OK"))?.AsButton();
            if (junkMsgBox != null) junkMsgBox.Click();

            // If we reached here without breaking or the app crashing, the test passes
            var currentWindow = _app.GetMainWindow(_automation);
            Assert.That(currentWindow, Is.Not.Null, "App should not have crashed");
        }
    }
}
