using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text;
using ReleasePrepTool.Models;
using ReleasePrepTool.Services;

namespace ReleasePrepTool.UI
{
    public partial class MainForm : Form
    {
        private TabControl tabControl = null!;
        
        // Tab 1: Configuration & Setup
        private TextBox txtProductName = null!, txtPgBinPath = null!, txtAiKey = null!, txtReleaseVersion = null!, txtReleasePath = null!;
        private Label lblOldDbStatus = null!, lblNewDbStatus = null!;
        private DatabaseConfig _oldDbConfig = null!, _newDbConfig = null!;
        private Button btnConnect = null!;
        
        // Tab 2: Databases Backup
        private Button btnBackupOld = null!;

        // Tab 3: Compare DB
        private SplitContainer splitCompare = null!;
        private TreeView treeSchema = null!;
        private Button btnLoadTables = null!;
        private ComboBox cmbSourceDb = null!, cmbTargetDb = null!;
        // Data Compare
        private ComboBox cmbSourceDataDb = null!, cmbTargetDataDb = null!;
        private Button btnLoadDataTables = null!, btnCompareData = null!;
        private DataGridView dgvTableDiffs = null!;
        // private TextBox txtDbDiffLog; // Moved to combined declaration

        // Tab 4: Sync & Execute DB
        private Button btnExecuteSchema = null!, btnExecuteData = null!;
        private TextBox txtDbDiffLog = null!, txtExecuteLog = null!, txtFinalExportLog = null!, txtBackupLog = null!, txtConfigDiffLog = null!, txtAiReviewLog = null!, txtDataLog = null!;
        private RichTextBox txtSourceDdl = null!, txtTargetDdl = null!;
        private DataGridView dgvJunkData = null!;
        private CheckedListBox clbDataTables = null!;
        private List<SchemaDiffResult> _schemaDiffs = new List<SchemaDiffResult>();
        private Button btnLoadJunkData = null!, btnDeleteJunkData = null!;
        private Label lblDataStatus = null!;
        
        // Tab 6: Compare Config
        private TextBox txtOldConfigPath = null!, txtNewConfigPath = null!;
        private Button btnSelectOldConfig = null!, btnSelectNewConfig = null!, btnCompareConfig = null!;    

        // Tab 7: Final Export + Tab 8: AI Review
        private Button btnReviewSchema = null!, btnReviewConfig = null!, btnGenerateSchema = null!;

        // Services
        private PostgresService? _oldPgService;
        private PostgresService? _newPgService;
        private DatabaseCompareService? _dbCompareService;
        private FileSystemService? _fileSystemService;
        private AIOperationService? _aiService;

        public MainForm()
        {
            InitializeComponent();
            SetupUI();
        }

        private void InitializeComponent()
        {
            this.Text = "Release Preparation Tool";
            this.Size = new Size(1000, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            
            tabControl = new TabControl { Dock = DockStyle.Fill };
            this.Controls.Add(tabControl);
        }

        private string OldDbName => _oldDbConfig?.DatabaseName ?? "old_db";
        private string NewDbName => _newDbConfig?.DatabaseName ?? "new_db";

        private void UpdateConnectionLabels()
        {
            lblOldDbStatus.Text = _oldDbConfig != null && !string.IsNullOrEmpty(_oldDbConfig.Host) ? $"[{(_oldDbConfig.IsValid ? "OK" : "Not Tested")}] {_oldDbConfig.Host}:{_oldDbConfig.Port} \nDB: {_oldDbConfig.DatabaseName}" : "Not Configured";
            lblNewDbStatus.Text = _newDbConfig != null && !string.IsNullOrEmpty(_newDbConfig.Host) ? $"[{(_newDbConfig.IsValid ? "OK" : "Not Tested")}] {_newDbConfig.Host}:{_newDbConfig.Port} \nDB: {_newDbConfig.DatabaseName}" : "Not Configured";
            
            lblOldDbStatus.ForeColor = _oldDbConfig?.IsValid == true ? Color.Green : Color.Black;
            lblNewDbStatus.ForeColor = _newDbConfig?.IsValid == true ? Color.Green : Color.Black;
        }

        private void SetupUI()
        {
            // 1. Config Setup Tab
            var tabConfig = new TabPage("1. Global Setup");
            var panelConfig = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            
            panelConfig.Controls.Add(new Label { 
                Text = "You can compare a source schema with a target schema to determine differences between them. You can then update the target schema to match the source schema for database objects you select.", 
                Width = 600, Height = 60, Margin = new Padding(0, 5, 0, 10), ForeColor = Color.DarkSlateGray 
            });

            panelConfig.Controls.Add(new Label { Text = "--- SOURCE CONNECTION (New/Dev) ---", Width = 500, Height = 25, Font = new Font(this.Font, FontStyle.Bold), Margin = new Padding(0, 5, 0, 5) });
            var pnlNewDb = new FlowLayoutPanel { Width = 600, Height = 40 };
            var btnConfigNewDb = new Button { Text = "Select Source Connection...", Width = 180, Margin = new Padding(5) };
            lblNewDbStatus = new Label { Text = "Not Configured", Width = 370, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left | AnchorStyles.Right, Padding = new Padding(10, 5, 0, 0) };
            btnConfigNewDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Source Database Connection", _newDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _newDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            pnlNewDb.Controls.Add(btnConfigNewDb); pnlNewDb.Controls.Add(lblNewDbStatus);
            panelConfig.Controls.Add(pnlNewDb);

            panelConfig.Controls.Add(new Label { Text = "--- TARGET CONNECTION (Old/Prod) ---", Width = 500, Height = 25, Font = new Font(this.Font, FontStyle.Bold), Margin = new Padding(0, 15, 0, 5) });
            var pnlOldDb = new FlowLayoutPanel { Width = 600, Height = 40 };
            var btnConfigOldDb = new Button { Text = "Select Target Connection...", Width = 180, Margin = new Padding(5) };
            lblOldDbStatus = new Label { Text = "Not Configured", Width = 370, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left | AnchorStyles.Right, Padding = new Padding(10, 5, 0, 0) };
            btnConfigOldDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Target Database Connection", _oldDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _oldDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            pnlOldDb.Controls.Add(btnConfigOldDb); pnlOldDb.Controls.Add(lblOldDbStatus);
            panelConfig.Controls.Add(pnlOldDb);

            panelConfig.Controls.Add(new Label { Text = "--- GENERAL SETTINGS ---", Width = 500, Height = 25, Font = new Font(this.Font, FontStyle.Bold), Margin = new Padding(0, 15, 0, 5) });
            txtPgBinPath = CreateBrowseRow(panelConfig, "PostgreSQL Bin Path (optional):", @"C:\Program Files\PostgreSQL\16\bin");
            txtProductName = CreateInputRow(panelConfig, "Product Name:", "app");
            txtReleaseVersion = CreateInputRow(panelConfig, "Release Version (e.g., 2.0):", "2.0");
            txtReleasePath = CreateBrowseRow(panelConfig, "Release Output Path:", @"C:\PROJECT_LCM\output");
            txtAiKey = CreateInputRow(panelConfig, "AI API Key (optional):", "", true);

            btnConnect = new Button { Text = "Initialize Services", Width = 150, Margin = new Padding(10) };
            btnConnect.Click += BtnConnect_Click;
            panelConfig.Controls.Add(btnConnect);
            tabConfig.Controls.Add(panelConfig);

            // 2. Restore DB Tab
            var tabBackup = new TabPage("2. Restore Databases");
            var panelBackup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var pnlRestoreRow = new FlowLayoutPanel { Width = 900, Height = 40 };
            btnBackupOld = new Button { Text = "Restore Old DB from File", Width = 200, Margin = new Padding(5) };
            var lblTargetDb = new Label { Text = "Target DB Name (empty = use connection name):", Width = 280, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(5) };
            var txtTargetDbName = new TextBox { Width = 200, Margin = new Padding(5), PlaceholderText = "e.g. my_old_db_v1" };
            btnBackupOld.Click += (s, e) => RestoreDbAsync(_oldPgService, OldDbName, txtTargetDbName.Text.Trim());
            pnlRestoreRow.Controls.Add(btnBackupOld);
            pnlRestoreRow.Controls.Add(lblTargetDb);
            pnlRestoreRow.Controls.Add(txtTargetDbName);

            txtBackupLog = new TextBox { Multiline = true, Width = 900, Height = 450, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            
            panelBackup.Controls.Add(pnlRestoreRow);
            panelBackup.Controls.Add(txtBackupLog);
            tabBackup.Controls.Add(panelBackup);


            // 3. Compare Schema Tab
            var tabCompareSchema = new TabPage("3. Compare Schema");
            splitCompare = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };

            // --- LEFT PANEL: table list ---
            var pnlLeft = new Panel { Dock = DockStyle.Fill };

            var pnlDbSelection = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(10, 10, 10, 0), BackColor = Color.White };
            var lblSrc = new Label { Text = "Source DB", Location = new Point(10, 12), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbSourceDb = new ComboBox { Location = new Point(90, 10), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblTgt = new Label { Text = "Target DB", Location = new Point(10, 42), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbTargetDb = new ComboBox { Location = new Point(90, 40), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            var btnRefreshDbs = new Button { Text = "↻ Refresh Databases", Location = new Point(90, 70), Width = 155, Height = 25, BackColor = Color.FromArgb(240, 240, 240) };
            btnRefreshDbs.Click += async (s, e) => await LoadDatabaseListsAsync();
            pnlDbSelection.Controls.AddRange(new Control[] { lblSrc, cmbSourceDb, lblTgt, cmbTargetDb, btnRefreshDbs });

            var pnlLeftToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 5, 0, 5), FlowDirection = FlowDirection.LeftToRight, BackColor = Color.White };
            var btnSelectAll = new Button { Text = "All", Width = 45, Margin = new Padding(2) };
            btnSelectAll.Click += (s, e) => SetTreeViewChecked(treeSchema.Nodes, true);
            var btnUnselectAll = new Button { Text = "None", Width = 55, Margin = new Padding(2) };
            btnUnselectAll.Click += (s, e) => SetTreeViewChecked(treeSchema.Nodes, false);
            btnLoadTables = new Button { Text = "Load Diffs", Width = 90, Margin = new Padding(2) };
            btnLoadTables.Click += BtnLoadTables_Click;
            
            pnlLeftToolbar.Controls.Add(btnSelectAll);
            pnlLeftToolbar.Controls.Add(btnUnselectAll);
            pnlLeftToolbar.Controls.Add(btnLoadTables);

            var lblHelp = new Label { Text = "← Select node to see DDL diff\n  Check ✓ for Data Compare", Dock = DockStyle.Bottom, Height = 36, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 7.5f), Padding = new Padding(5,0,0,0) };
            treeSchema = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, BorderStyle = BorderStyle.None, ShowNodeToolTips = true };
            treeSchema.AfterSelect += TreeSchema_AfterSelect;
            treeSchema.AfterCheck += TreeSchema_AfterCheck;

            pnlLeft.Controls.Add(treeSchema);
            pnlLeft.Controls.Add(lblHelp);
            pnlLeft.Controls.Add(pnlLeftToolbar);
            pnlLeft.Controls.Add(pnlDbSelection);
            splitCompare.Panel1.Controls.Add(pnlLeft);

            // --- RIGHT PANEL: action bar + 3-pane DDL view ---
            var pnlRight = new Panel { Dock = DockStyle.Fill };
            var pnlActionBar = new Panel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5), BackColor = Color.White };
            btnGenerateSchema = new Button { Text = "Export Schema Script", Width = 160, Height = 30, Location = new Point(5, 5), BackColor = Color.FromArgb(240, 240, 240) };
            btnGenerateSchema.Click += BtnGenerateSchema_Click;
            txtDbDiffLog = new TextBox { Width = 600, Height = 35, Location = new Point(175, 5), BorderStyle = BorderStyle.None, ReadOnly = true, BackColor = Color.White, Multiline = true, Font = new Font(this.Font.FontFamily, 8f) };
            pnlActionBar.Controls.Add(btnGenerateSchema);
            pnlActionBar.Controls.Add(txtDbDiffLog);

            var pnlHeaders = new TableLayoutPanel { Dock = DockStyle.Top, Height = 28, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(240, 240, 240) };
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlHeaders.Controls.Add(new Label { Text = "Source DDL (Old DB)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10,0,0,0) }, 0, 0);
            pnlHeaders.Controls.Add(new Label { Text = "Target DDL (New DB)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10,0,0,0) }, 1, 0);

            var pnlDdl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            txtSourceDdl = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None, WordWrap = false };
            txtTargetDdl = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None, WordWrap = false };
            pnlDdl.Controls.Add(txtSourceDdl, 0, 0);
            pnlDdl.Controls.Add(txtTargetDdl, 1, 0);

            pnlRight.Controls.Add(pnlDdl);
            pnlRight.Controls.Add(pnlHeaders);
            pnlRight.Controls.Add(pnlActionBar);

            splitCompare.Panel2.Controls.Add(pnlRight);
            tabCompareSchema.Controls.Add(splitCompare);
            
            // Force SplitterDistance again after adding to parent to ensure it sticks
            splitCompare.SplitterDistance = 250;
            
            // treeSchema handles its own events


            // 4. Compare Data Tab
            var tabCompareData = new TabPage("4. Compare Data");
            var pnlDataMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            
            // Top: VS-Style Setup Panel
            var pnlDataSetup = new TableLayoutPanel { Dock = DockStyle.Top, Height = 100, ColumnCount = 3, Padding = new Padding(0) };
            pnlDataSetup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            pnlDataSetup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            pnlDataSetup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            var gbSource = new GroupBox { Text = "Source Database (New/Dev)", Dock = DockStyle.Fill, Padding = new Padding(10, 15, 10, 10) };
            cmbSourceDataDb = new ComboBox { Name = "cmbSourceDataDb", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSourceDataDb.SelectedIndexChanged += (s, e) => { if (!string.IsNullOrEmpty(cmbSourceDataDb.Text) && !string.IsNullOrEmpty(cmbTargetDataDb.Text)) BtnLoadDataTables_Click(null!, null!); };
            gbSource.Controls.Add(cmbSourceDataDb);
            pnlDataSetup.Controls.Add(gbSource, 0, 0);

            var lblArrow = new Label { Text = "↔", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24f), ForeColor = Color.Gray };
            pnlDataSetup.Controls.Add(lblArrow, 1, 0);

            var gbTarget = new GroupBox { Text = "Target Database (Old/Prod)", Dock = DockStyle.Fill, Padding = new Padding(10, 15, 10, 10) };
            cmbTargetDataDb = new ComboBox { Name = "cmbTargetDataDb", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTargetDataDb.SelectedIndexChanged += (s, e) => { if (!string.IsNullOrEmpty(cmbSourceDataDb.Text) && !string.IsNullOrEmpty(cmbTargetDataDb.Text)) BtnLoadDataTables_Click(null!, null!); };
            gbTarget.Controls.Add(cmbTargetDataDb);
            pnlDataSetup.Controls.Add(gbTarget, 2, 0);

            var pnlDataActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 10, 0, 0) };
            // btnLoadDataTables added automatically via ComboBox selection

            btnCompareData = new Button { Name = "btnCompareData", Text = "Start Comparison", Width = 160, Height = 32, Font = new Font(this.Font, FontStyle.Bold), BackColor = Color.AliceBlue };
            btnCompareData.Click += BtnCompareData_Click;
            pnlDataActions.Controls.Add(btnCompareData);

            var btnGenerateData = new Button { Text = "Export Sync Script", Width = 160, Height = 32 };
            btnGenerateData.Click += BtnGenerateData_Click;
            pnlDataActions.Controls.Add(btnGenerateData);

            lblDataStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(10, 10, 0, 0), Font = new Font(this.Font, FontStyle.Italic), ForeColor = Color.Blue };
            pnlDataActions.Controls.Add(lblDataStatus);

            // Main: DataGridView for Table Summary
            dgvTableDiffs = new DataGridView { 
                Dock = DockStyle.Fill, 
                BackgroundColor = Color.FromArgb(250, 250, 250),
                ColumnHeadersVisible = true,
                BorderStyle = BorderStyle.FixedSingle,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Name = "dgvTableDiffs",
                Margin = new Padding(0, 10, 0, 0)
            };
            dgvTableDiffs.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ColCheck", HeaderText = "", Width = 30, FillWeight = 5 });
            dgvTableDiffs.Columns.Add("ColName", "Table Name");
            dgvTableDiffs.Columns.Add("ColDiff", "Different");
            dgvTableDiffs.Columns.Add("ColSource", "Only in Source");
            dgvTableDiffs.Columns.Add("ColTarget", "Only in Target");
            dgvTableDiffs.Columns.Add("ColIdentical", "Identical");
            dgvTableDiffs.CellDoubleClick += DgvTableDiffs_CellDoubleClick;

            pnlDataMain.Controls.Add(dgvTableDiffs);
            pnlDataMain.Controls.Add(pnlDataActions);
            pnlDataMain.Controls.Add(pnlDataSetup);
            dgvTableDiffs.BringToFront(); // Ensure Fill control docks last and is visible in the remaining space
            
            var lblHint = new Label { Text = "* Hint: Double-click a row to see detailed data differences.", Dock = DockStyle.Bottom, Height = 25, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font, FontStyle.Italic) };
            pnlDataMain.Controls.Add(lblHint);
            lblHint.SendToBack(); // Hint at the very bottom

            tabCompareData.Controls.Add(pnlDataMain);


            // 5. Execute Sync Tab
            var tabSyncDb = new TabPage("5. Execute Sync");
            var panelSync = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var pnlSyncBtns = new FlowLayoutPanel { Width = 900, Height = 45 };
            btnExecuteSchema = new Button { Text = "Execute Schema Sync to Old DB", Width = 250, Margin = new Padding(5) };
            btnExecuteSchema.Click += BtnExecuteSchema_Click;
            btnExecuteData = new Button { Text = "Execute Data Sync to Old DB", Width = 250, Margin = new Padding(5) };
            btnExecuteData.Click += BtnExecuteData_Click;
            var btnVerifySync = new Button { Text = "Verify Sync Status", Width = 200, Margin = new Padding(5) };
            btnVerifySync.Click += BtnVerifySync_Click;
            
            pnlSyncBtns.Controls.Add(btnExecuteSchema); pnlSyncBtns.Controls.Add(btnExecuteData); pnlSyncBtns.Controls.Add(btnVerifySync);
            txtExecuteLog = new TextBox { Multiline = true, Width = 900, Height = 450, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            panelSync.Controls.Add(pnlSyncBtns);
            panelSync.Controls.Add(txtExecuteLog);
            tabSyncDb.Controls.Add(panelSync);

            // 6. Clean Junk Data Tab
            var tabCleanJunk = new TabPage("6. Clean Junk");
            dgvJunkData = new DataGridView { Width = 900, Height = 450, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            btnLoadJunkData = new Button { Text = "Scan Junk Data", Width = 200, Margin = new Padding(5) };
            
            var txtJunkKeyword = new TextBox { Width = 150, Text = "test", Margin = new Padding(5) };
            btnLoadJunkData.Click += async (s, e) => await LoadJunkDataAsync(txtJunkKeyword.Text);
            
            btnDeleteJunkData = new Button { Text = "Delete Selected Rows", Width = 150, Margin = new Padding(5) };
            btnDeleteJunkData.Click += BtnDeleteJunkData_Click;
            
            var panelCleanLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var pnlBtns = new FlowLayoutPanel { Width = 900, Height = 40 };
            
            pnlBtns.Controls.Add(new Label { Text = "Keyword:", Width = 60, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 10, 0, 0) });
            pnlBtns.Controls.Add(txtJunkKeyword);
            pnlBtns.Controls.Add(btnLoadJunkData);
            pnlBtns.Controls.Add(btnDeleteJunkData);
            panelCleanLayout.Controls.Add(pnlBtns);
            panelCleanLayout.Controls.Add(dgvJunkData);
            tabCleanJunk.Controls.Add(panelCleanLayout);

            // 7. Final Export
            var tabFinalExport = new TabPage("7. Export Final Release DB");
            var pnlFinal = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var btnExportFinal = new Button { Text = "Export Old DB (Backup + SQL)", Width = 300, Margin = new Padding(5) };
            btnExportFinal.Click += BtnExportFinal_Click;
            txtFinalExportLog = new TextBox { Multiline = true, Width = 900, Height = 450, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            pnlFinal.Controls.Add(btnExportFinal); pnlFinal.Controls.Add(txtFinalExportLog);
            tabFinalExport.Controls.Add(pnlFinal);

            // 8. Config Compare Tab
            var tabConfigCompare = new TabPage("8. Compare Config");
            var pnlConfigBtns = new FlowLayoutPanel { Width = 900, Height = 40 };
            txtOldConfigPath = new TextBox { Width = 300 };
            txtNewConfigPath = new TextBox { Width = 300 };
            btnSelectOldConfig = new Button { Text = "Old Config" };
            btnSelectOldConfig.Click += (s, e) => SelectFile(txtOldConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
            btnSelectNewConfig = new Button { Text = "New Config" };
            btnSelectNewConfig.Click += (s, e) => SelectFile(txtNewConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
            btnCompareConfig = new Button { Text = "Compare & Generate Note" };
            btnCompareConfig.Click += BtnCompareConfig_Click;
            
            pnlConfigBtns.Controls.Add(btnSelectOldConfig); pnlConfigBtns.Controls.Add(txtOldConfigPath);
            pnlConfigBtns.Controls.Add(btnSelectNewConfig); pnlConfigBtns.Controls.Add(txtNewConfigPath);
            pnlConfigBtns.Controls.Add(btnCompareConfig);

            txtConfigDiffLog = new TextBox { Multiline = true, Width = 900, Height = 450, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            var pnlConfigMain = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            pnlConfigMain.Controls.Add(pnlConfigBtns);
            pnlConfigMain.Controls.Add(txtConfigDiffLog);
            tabConfigCompare.Controls.Add(pnlConfigMain);

            // 9. AI Review Tab
            var tabAi = new TabPage("9. AI Review");
            var pnlAi = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            btnReviewSchema = new Button { Text = "Review Schema Script", Width = 200, Margin = new Padding(5) };
            btnReviewSchema.Click += BtnReviewSchema_Click;
            btnReviewConfig = new Button { Text = "Review Config Diff", Width = 200, Margin = new Padding(5) };
            btnReviewConfig.Click += BtnReviewConfig_Click;
            txtAiReviewLog = new TextBox { Multiline = true, Width = 900, Height = 450, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            pnlAi.Controls.Add(btnReviewSchema);
            pnlAi.Controls.Add(btnReviewConfig);
            pnlAi.Controls.Add(txtAiReviewLog);
            tabAi.Controls.Add(pnlAi);



            // Add all tabs
            tabControl.TabPages.Add(tabConfig);          // 1
            tabControl.TabPages.Add(tabBackup);          // 2
            tabControl.TabPages.Add(tabCompareSchema);    // 3
            tabControl.TabPages.Add(tabCompareData);      // 4
            tabControl.TabPages.Add(tabSyncDb);           // 5
            tabControl.TabPages.Add(tabCleanJunk);        // 6
            tabControl.TabPages.Add(tabFinalExport);      // 7
            tabControl.TabPages.Add(tabConfigCompare);    // 8
            tabControl.TabPages.Add(tabAi);               // 9

            tabControl.SelectedIndexChanged += async (s, e) => {
                if (tabControl.SelectedTab == tabCompareSchema && cmbSourceDb.Items.Count == 0)
                    await LoadDatabaseListsAsync();
            };
            
            this.FormClosing += (s, e) => SaveConfig();
            this.Load += (s, e) => {
                LoadConfig();
                // Ensure SplitterDistance is set after layout is complete
                if (splitCompare != null) splitCompare.SplitterDistance = 260;
            };
        }

        private TextBox CreateBrowseRow(FlowLayoutPanel parent, string labelText, string defaultValue, bool isFolder = true)
        {
            var pnl = new FlowLayoutPanel { Width = 600, Height = 35 };
            var lbl = new Label { Text = labelText, Width = 200, TextAlign = ContentAlignment.MiddleRight };
            var txt = new TextBox { Text = defaultValue, Width = 300 };
            var btn = new Button { Text = "...", Width = 40 };
            btn.Click += (s, e) => {
                if (isFolder) {
                    using (var fbd = new FolderBrowserDialog { SelectedPath = txt.Text }) {
                        if (fbd.ShowDialog() == DialogResult.OK) txt.Text = fbd.SelectedPath;
                    }
                }
            };
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(txt);
            pnl.Controls.Add(btn);
            parent.Controls.Add(pnl);
            return txt;
        }

        private bool EnsureServicesInitialized()
        {
            if (_oldPgService != null && _newPgService != null && _dbCompareService != null && _fileSystemService != null)
                return true;

            if (_oldDbConfig == null || _newDbConfig == null)
            {
                MessageBox.Show("Please select connections for both Old and New databases first.");
                return false;
            }

            try
            {
                _oldPgService = new PostgresService(_oldDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _newPgService = new PostgresService(_newDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _dbCompareService = new DatabaseCompareService(_oldDbConfig!, _newDbConfig!);
                _fileSystemService = new FileSystemService(txtReleasePath.Text, txtReleaseVersion.Text, txtProductName.Text);
                _aiService = new AIOperationService(txtAiKey.Text);

                _fileSystemService.EnsureDirectoryStructure();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to auto-initialize services: {ex.Message}");
                return false;
            }
        }

        private TextBox CreateInputRow(FlowLayoutPanel parent, string labelText, string defaultValue, bool isPassword = false)
        {
            var pnl = new FlowLayoutPanel { Width = 600, Height = 35 };
            var lbl = new Label { Text = labelText, Width = 200, TextAlign = ContentAlignment.MiddleRight };
            var txt = new TextBox { Text = defaultValue, Width = 350, UseSystemPasswordChar = isPassword };
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(txt);
            parent.Controls.Add(pnl);
            return txt;
        }

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_oldDbConfig == null || _newDbConfig == null) {
                MessageBox.Show("Please select connections for both Old and New databases before initializing.");
                return;
            }
            try
            {
                _oldPgService = new PostgresService(_oldDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _newPgService = new PostgresService(_newDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _dbCompareService = new DatabaseCompareService(_oldDbConfig!, _newDbConfig!);
                _fileSystemService = new FileSystemService(txtReleasePath.Text, txtReleaseVersion.Text, txtProductName.Text);
                _aiService = new AIOperationService(txtAiKey.Text);

                _fileSystemService.EnsureDirectoryStructure();
                SaveConfig(); 
                await LoadDatabaseListsAsync(); // Auto-load DB lists for comparison
                MessageBox.Show($"Services initialized and directory structure created at {_fileSystemService.BaseReleasePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing services: {ex.Message}");
            }
        }

        private async void RestoreDbAsync(PostgresService service, string dbName, string targetDbName = "")
        {
            if (!EnsureServicesInitialized()) return;
            // The service parameter might still be the old null reference if passed before auto-init.
            // Re-assign from fields if necessary.
            var activeService = (service == null) ? (dbName == OldDbName ? _oldPgService : _newPgService) : service;
            if (activeService == null) return;

            var effectiveName = string.IsNullOrWhiteSpace(targetDbName) ? dbName : targetDbName;
            using (var ofd = new OpenFileDialog { Filter = "Backup Files (*.backup)|*.backup|SQL Scripts (*.sql)|*.sql" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtBackupLog.AppendText($"Starting restore into '{effectiveName}' from {ofd.FileName}...\r\n");
                    btnBackupOld.Enabled = false;
                    try
                    {
                        var ext = Path.GetExtension(ofd.FileName).ToLower();
                        // Thread-safe callback: marshal each line onto the UI thread
                        Action<string> onOutput = line =>
                        {
                            if (txtBackupLog.InvokeRequired)
                                txtBackupLog.Invoke(() => txtBackupLog.AppendText(line + "\r\n"));
                            else
                                txtBackupLog.AppendText(line + "\r\n");
                        };
                        await service.RestoreDatabaseAsync(ext, ofd.FileName, string.IsNullOrWhiteSpace(targetDbName) ? null : targetDbName, onOutput);
                        txtBackupLog.AppendText($"✅ Restore into '{effectiveName}' completed successfully.\r\n");
                    }
                    catch (Exception ex)
                    {
                        txtBackupLog.AppendText($"❌ Error during restore: {ex.Message}\r\n");
                    }
                    finally
                    {
                        btnBackupOld.Enabled = true;
                    }
                }
            }
        }


        private void TreeSchema_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null || e.Node.Parent == null) return;
            var objectName = e.Node.Text;
            if (objectName.Contains("] ")) objectName = objectName.Substring(objectName.IndexOf("] ") + 2);

            var diff = _schemaDiffs?.FirstOrDefault(d => d.ObjectName == objectName);
            if (diff != null) {
                UpdateDiffView(diff.SourceDDL, diff.TargetDDL);
            } else {
                txtSourceDdl.Clear();
                txtTargetDdl.Clear();
                txtSourceDdl.AppendText("-- Identical/No changes detected.");
                txtTargetDdl.AppendText("-- Identical/No changes detected.");
            }
        }

        private void UpdateDiffView(string source, string target)
        {
            txtSourceDdl.Clear();
            txtTargetDdl.Clear();

            var sLines = (source ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var tLines = (target ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int max = Math.Max(sLines.Length, tLines.Length);

            for (int i = 0; i < max; i++)
            {
                string sLine = i < sLines.Length ? sLines[i] : null;
                string tLine = i < tLines.Length ? tLines[i] : null;

                if (sLine == tLine)
                {
                    AppendHighlightedLine(txtSourceDdl, sLine, Color.White);
                    AppendHighlightedLine(txtTargetDdl, tLine, Color.White);
                }
                else if (sLine != null && tLine != null)
                {
                    // Changed line
                    AppendHighlightedLine(txtSourceDdl, sLine, Color.FromArgb(230, 240, 255)); // Light Blue
                    AppendHighlightedLine(txtTargetDdl, tLine, Color.FromArgb(230, 240, 255));
                }
                else if (sLine != null)
                {
                    // Removed line
                    AppendHighlightedLine(txtSourceDdl, sLine, Color.FromArgb(255, 230, 230)); // Light Pink
                    AppendHighlightedLine(txtTargetDdl, "", Color.White);
                }
                else if (tLine != null)
                {
                    // Added line
                    AppendHighlightedLine(txtSourceDdl, "", Color.White);
                    AppendHighlightedLine(txtTargetDdl, tLine, Color.FromArgb(230, 255, 230)); // Light Green
                }
            }
        }

        private void AppendHighlightedLine(RichTextBox rtb, string text, Color backColor)
        {
            if (text == null) return;
            int start = rtb.TextLength;
            rtb.AppendText(text + "\n");
            int end = rtb.TextLength;
            rtb.Select(start, end - start);
            rtb.SelectionBackColor = backColor;
            rtb.DeselectAll();
        }

        private void TreeSchema_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // Sync children only if it's a root node
            if (e.Action != TreeViewAction.Unknown && e.Node.Nodes.Count > 0)
            {
                SetTreeViewChecked(e.Node.Nodes, e.Node.Checked);
            }
        }

        private void SetTreeViewChecked(TreeNodeCollection nodes, bool isChecked)
        {
            foreach (TreeNode node in nodes)
            {
                node.Checked = isChecked;
                if (node.Nodes.Count > 0) SetTreeViewChecked(node.Nodes, isChecked);
            }
        }

        private void ApplyDiffColors(TreeNode node, string diffType)
        {
            switch (diffType)
            {
                case "Added": node.ForeColor = Color.Green; node.Text = "[NEW] " + node.Text; break;
                case "Removed": node.ForeColor = Color.Red; node.Text = "[REMOVED] " + node.Text; break;
                case "Altered": node.ForeColor = Color.Blue; node.Text = "[DIFF] " + node.Text; break;
            }
        }

        private async void BtnLoadTables_Click(object sender, EventArgs e)
        {
            if (cmbSourceDb.SelectedItem == null || cmbTargetDb.SelectedItem == null) {
                MessageBox.Show("Please select both Source and Target databases from the list.");
                return;
            }

            // Update configs with selected database names
            _oldDbConfig.DatabaseName = cmbSourceDb.SelectedItem.ToString();
            _newDbConfig.DatabaseName = cmbTargetDb.SelectedItem.ToString();
            
            // Force re-initialization of services for the new databases
            _oldPgService = null;
            _newPgService = null;
            _dbCompareService = null;

            if (!EnsureServicesInitialized()) return;
            treeSchema.Nodes.Clear();
            btnLoadTables.Text = "Loading...";
            btnLoadTables.Enabled = false;
            try
            {
                txtDbDiffLog.Text = "Generating schema diffs...";
                _schemaDiffs = await _dbCompareService.GenerateSchemaDiffResultsAsync();

                var oldTables = await _oldPgService.GetTablesAsync();
                var newTables = await _newPgService.GetTablesAsync();
                var allTables = oldTables.Union(newTables).OrderBy(t => t).ToList();
                var commonTables = oldTables.Intersect(newTables).OrderBy(t => t).ToList();

                // Update Tab 4's table list
                clbDataTables.Items.Clear();
                foreach (var table in commonTables) clbDataTables.Items.Add(table);

                var tableRoot = new TreeNode("Tables");
                var viewRoot = new TreeNode("Views");
                var routineRoot = new TreeNode("Functions");
                var indexRoot = new TreeNode("Indexes");
                var triggerRoot = new TreeNode("Triggers");
                var constraintRoot = new TreeNode("Constraints");

                foreach (var table in allTables)
                {
                    var diff = _schemaDiffs.FirstOrDefault(d => d.ObjectName == table && d.ObjectType == "Table");
                    var node = new TreeNode(table);
                    if (diff != null) ApplyDiffColors(node, diff.DiffType);
                    tableRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "View"))
                {
                    var node = new TreeNode(diff.ObjectName);
                    ApplyDiffColors(node, diff.DiffType);
                    viewRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Routine"))
                {
                    var node = new TreeNode(diff.ObjectName);
                    ApplyDiffColors(node, diff.DiffType);
                    routineRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Index"))
                {
                    var node = new TreeNode(diff.ObjectName);
                    ApplyDiffColors(node, diff.DiffType);
                    indexRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Trigger"))
                {
                    var node = new TreeNode(diff.ObjectName);
                    ApplyDiffColors(node, diff.DiffType);
                    triggerRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Constraint"))
                {
                    var node = new TreeNode(diff.ObjectName);
                    ApplyDiffColors(node, diff.DiffType);
                    constraintRoot.Nodes.Add(node);
                }

                if (tableRoot.Nodes.Count > 0) treeSchema.Nodes.Add(tableRoot);
                if (viewRoot.Nodes.Count > 0) treeSchema.Nodes.Add(viewRoot);
                if (routineRoot.Nodes.Count > 0) treeSchema.Nodes.Add(routineRoot);
                if (indexRoot.Nodes.Count > 0) treeSchema.Nodes.Add(indexRoot);
                if (triggerRoot.Nodes.Count > 0) treeSchema.Nodes.Add(triggerRoot);
                if (constraintRoot.Nodes.Count > 0) treeSchema.Nodes.Add(constraintRoot);
                
                tableRoot.Expand();
                txtDbDiffLog.Text = $"Found {_schemaDiffs.Count} total schema differences (Tables, Views, Functions, Indexes, etc.)";
            }
            catch (Exception ex)
            {
                txtDbDiffLog.Text = $"Error: {ex.Message}";
            }
            finally
            {
                btnLoadTables.Text = "Load Diffs";
                btnLoadTables.Enabled = true;
            }
        }

        private async void BtnGenerateSchema_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            txtDbDiffLog.Text = "Generating schema diff script...";
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"-- Schema update script from {OldDbName} to {NewDbName}");
                sb.AppendLine($"-- Generated at {DateTime.Now}\n");

                foreach (var r in _schemaDiffs)
                {
                    sb.AppendLine($"-- {r.ObjectType}: {r.ObjectName} ({r.DiffType})");
                    sb.AppendLine(r.DiffScript);
                    sb.AppendLine();
                }

                var sql = sb.ToString();
                var path = _fileSystemService.GetSqlScriptPath(NewDbName, true);
                _fileSystemService.WriteToFile(path, sql);
                txtDbDiffLog.Text = $"Schema script exported to {path}";
            }
            catch (Exception ex)
            {
                txtDbDiffLog.Text = $"Error: {ex.Message}";
            }
        }


        private void SaveConfig()
        {
            try {
                var config = new {
                    Product = txtProductName.Text, 
                    OldConfig = _oldDbConfig,
                    NewConfig = _newDbConfig,
                    PgBin = txtPgBinPath.Text, Version = txtReleaseVersion.Text, 
                    ReleasePath = txtReleasePath.Text, AiKey = txtAiKey.Text
                };
                File.WriteAllText("appsettings.local.json", Newtonsoft.Json.JsonConvert.SerializeObject(config));
            } catch { }
        }

        private void LoadConfig()
        {
            try {
                if (File.Exists("appsettings.local.json")) {
                    var json = File.ReadAllText("appsettings.local.json");
                    dynamic config = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    if (config != null) {
                        txtProductName.Text = (string)config.Product ?? txtProductName.Text;
                        if (config.OldConfig != null) _oldDbConfig = config.OldConfig.ToObject<DatabaseConfig>();
                        if (config.NewConfig != null) _newDbConfig = config.NewConfig.ToObject<DatabaseConfig>();
                        txtPgBinPath.Text = (string)config.PgBin ?? txtPgBinPath.Text; txtReleaseVersion.Text = (string)config.Version ?? txtReleaseVersion.Text;
                        txtReleasePath.Text = (string)config.ReleasePath ?? txtReleasePath.Text; txtAiKey.Text = (string)config.AiKey ?? txtAiKey.Text;
                        UpdateConnectionLabels();
                    }
                }
            } catch { }
        }

        private async Task LoadDatabaseListsAsync()
        {
            if (_oldDbConfig == null || _newDbConfig == null) {
                MessageBox.Show("Please select connections in Global Setup first.");
                return;
            }

            try {
                // Temporary services just to list databases
                var oldSvc = new PostgresService(_oldDbConfig);
                var newSvc = new PostgresService(_newDbConfig);

                var oldDbs = await oldSvc.GetAllDatabasesAsync();
                var newDbs = await newSvc.GetAllDatabasesAsync();

                cmbSourceDb.Items.Clear();
                cmbSourceDb.Items.AddRange(oldDbs.ToArray());
                cmbSourceDataDb.Items.Clear();
                cmbSourceDataDb.Items.AddRange(oldDbs.ToArray());
                
                if (oldDbs.Contains(_oldDbConfig.DatabaseName)) {
                    cmbSourceDb.SelectedItem = _oldDbConfig.DatabaseName;
                    cmbSourceDataDb.SelectedItem = _oldDbConfig.DatabaseName;
                }
                else if (oldDbs.Any()) {
                    cmbSourceDb.SelectedIndex = 0;
                    cmbSourceDataDb.SelectedIndex = 0;
                }

                cmbTargetDb.Items.Clear();
                cmbTargetDb.Items.AddRange(newDbs.ToArray());
                cmbTargetDataDb.Items.Clear();
                cmbTargetDataDb.Items.AddRange(newDbs.ToArray());

                if (newDbs.Contains(_newDbConfig.DatabaseName)) {
                    cmbTargetDb.SelectedItem = _newDbConfig.DatabaseName;
                    cmbTargetDataDb.SelectedItem = _newDbConfig.DatabaseName;
                }
                else if (newDbs.Any()) {
                    cmbTargetDb.SelectedIndex = 0;
                    cmbTargetDataDb.SelectedIndex = 0;
                }
            }
            catch (Exception ex) {
                MessageBox.Show($"Error loading database lists: {ex.Message}");
            }
        }
        private async void BtnLoadDataTables_Click(object sender, EventArgs e)
        {
            if (cmbSourceDataDb.SelectedItem == null || cmbTargetDataDb.SelectedItem == null) {
                MessageBox.Show("Please select both Source and Target databases.");
                return;
            }

            _oldDbConfig.DatabaseName = cmbSourceDataDb.SelectedItem.ToString()!;
            _newDbConfig.DatabaseName = cmbTargetDataDb.SelectedItem.ToString()!;
            _dbCompareService = null; // Force recreation with new DBs

            if (!EnsureServicesInitialized()) return;
            
            lblDataStatus.Text = "⌛ Loading common tables...";
            lblDataStatus.ForeColor = Color.Blue;
            this.Cursor = Cursors.WaitCursor;
            
            try
            {
                var sourceTables = await _newPgService!.GetTablesAsync();
                var targetTables = await _oldPgService!.GetTablesAsync();
                
                var allTables = sourceTables.Union(targetTables).OrderBy(t => t).ToList();

                dgvTableDiffs.Rows.Clear();
                foreach (var table in allTables)
                {
                    bool inSource = sourceTables.Contains(table);
                    bool inTarget = targetTables.Contains(table);
                    
                    int rowIdx = dgvTableDiffs.Rows.Add(inSource && inTarget, table, "", "", "", "");
                    var row = dgvTableDiffs.Rows[rowIdx];
                    
                    if (inSource && !inTarget)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 240, 255); // Light Blue (Added)
                        row.Cells["ColIdentical"].Value = "Added (New)";
                        row.Cells["ColCheck"].Value = false;
                        row.Cells["ColCheck"].ReadOnly = true;
                    }
                    else if (!inSource && inTarget)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 230); // Light Pink (Removed)
                        row.Cells["ColIdentical"].Value = "Removed (Old)";
                        row.Cells["ColCheck"].Value = false;
                        row.Cells["ColCheck"].ReadOnly = true;
                    }
                    else
                    {
                        row.Cells["ColIdentical"].Value = "Common";
                    }
                }

                if (!allTables.Any())
                    lblDataStatus.Text = "ℹ️ No tables found in either database.";
                else
                    lblDataStatus.Text = $"✅ Found {allTables.Count} tables total ({sourceTables.Intersect(targetTables).Count()} common).";
            }
            catch (Exception ex)
            {
                lblDataStatus.Text = "❌ Error loading tables.";
                MessageBox.Show($"Error loading tables: {ex.Message}");
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        private async void BtnCompareData_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            
            var checkedRows = dgvTableDiffs.Rows.Cast<DataGridViewRow>()
                .Where(r => Convert.ToBoolean(r.Cells["ColCheck"].Value))
                .ToList();

            if (!checkedRows.Any()) { MessageBox.Show("Please select tables to compare."); return; }

            btnCompareData.Enabled = false;
            btnCompareData.Text = "Comparing...";
            lblDataStatus.Text = "⌛ Comparing table data...";
            this.Cursor = Cursors.WaitCursor;

            try
            {
                foreach (var row in checkedRows)
                {
                    string table = row.Cells["ColName"].Value.ToString()!;
                    row.Cells["ColDiff"].Value = "...";
                    
                    var summary = await _dbCompareService.GetTableDataDiffSummaryAsync(table);
                    
                    row.Cells["ColDiff"].Value = summary.UpdatedCount;
                    row.Cells["ColSource"].Value = summary.InsertedCount;
                    row.Cells["ColTarget"].Value = summary.DeletedCount;
                    row.Cells["ColIdentical"].Value = summary.HasDifferences ? "Different" : "Synchronized";
                    
                    if (summary.HasDifferences)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 224); // Light Yellow (Changed)
                        row.DefaultCellStyle.ForeColor = Color.DarkRed;
                        row.Cells["ColIdentical"].Value = "Different";
                    }
                    else
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 230); // Light Green (Synchronized)
                        row.DefaultCellStyle.ForeColor = Color.DarkGreen;
                        row.Cells["ColIdentical"].Value = "Synchronized";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during comparison: {ex.Message}");
            }
            finally
            {
                btnCompareData.Enabled = true;
                btnCompareData.Text = "Start Comparison";
                lblDataStatus.Text = "✅ Data comparison complete.";
                this.Cursor = Cursors.Default;
            }
        }

        private async void DgvTableDiffs_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var table = dgvTableDiffs.Rows[e.RowIndex].Cells["ColName"].Value?.ToString();
            if (string.IsNullOrEmpty(table)) return;

            if (!EnsureServicesInitialized()) return;

            try
            {
                var diffs = await _dbCompareService.GetDetailedTableDataDiffAsync(table);
                using (var dlg = new DataDiffDialog(table, diffs))
                {
                    dlg.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading details for {table}: {ex.Message}");
            }
        }

        private async void BtnGenerateData_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;

            var selectedTables = dgvTableDiffs.Rows.Cast<DataGridViewRow>()
                .Where(r => Convert.ToBoolean(r.Cells["ColCheck"].Value))
                .Select(r => r.Cells["ColName"].Value.ToString()!)
                .ToList();

            if (!selectedTables.Any())
            {
                MessageBox.Show("Please select at least one table.");
                return;
            }

            try
            {
                var script = await _dbCompareService.GenerateDataDiffAsync(selectedTables);
                var path = _fileSystemService.GetSqlScriptPath(_newDbConfig.DatabaseName, false);
                _fileSystemService.WriteToFile(path, script);
                MessageBox.Show($"Data sync script generated at: {path}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating script: {ex.Message}");
            }
        }

        private async void BtnExecuteSchema_Click(object sender, EventArgs e)
        {
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, true);
            await ExecuteScriptOnOldDb(path, "Schema");
        }

        private async void BtnExecuteData_Click(object sender, EventArgs e)
        {
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, false);
            await ExecuteScriptOnOldDb(path, "Data");
        }

        private async Task ExecuteScriptOnOldDb(string scriptPath, string type)
        {
            if (!EnsureServicesInitialized()) return;
            if (!File.Exists(scriptPath)) { MessageBox.Show($"Script {scriptPath} not found."); return; }
            try
            {
                txtExecuteLog.AppendText($"Executing {type} script on old database...\r\n");
                var sql = File.ReadAllText(scriptPath);
                
                await _oldPgService!.ExecuteSqlWithTransactionAsync(sql);
                
                txtExecuteLog.AppendText($"{type} sync executed successfully.\r\n");
            }
            catch (Exception ex)
            {
                txtExecuteLog.AppendText($"Execution failed, transaction rolled back. Error: {ex.Message}\r\n");
            }
        }

        private async void BtnExportFinal_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            try
            {
                txtFinalExportLog.AppendText($"Starting final export for {OldDbName}...\r\n");
                var backupPath = _fileSystemService.GetBackupPath(OldDbName);
                await _oldPgService.BackupDatabaseAsync(backupPath);
                txtFinalExportLog.AppendText($"Backup saved to {backupPath}\r\n");

                var fullPath = _fileSystemService.GetFullScriptPath(OldDbName);
                await _oldPgService.DumpFullScriptAsync(fullPath);
                txtFinalExportLog.AppendText($"Full script saved to {fullPath}\r\n");
            }
            catch (Exception ex)
            {
                txtFinalExportLog.AppendText($"Error: {ex.Message}\r\n");
            }
        }

        private async void BtnVerifySync_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            txtExecuteLog.AppendText("Verifying Sync Status...\r\n");
            try
            {
                var schemaScript = await _dbCompareService.GenerateSchemaDiffAsync();
                if (string.IsNullOrWhiteSpace(schemaScript) || (!schemaScript.Contains("ALTER TABLE") && !schemaScript.Contains("CREATE TABLE") && !schemaScript.Contains("DROP ")))
                {
                    txtExecuteLog.AppendText("-> Schema Verification Passed! No remaining schema differences.\r\n");
                }
                else
                {
                    txtExecuteLog.AppendText("-> Schema Verification FAILED! There are still schema differences. Please check Tab 3.\r\n");
                }
                
                var selectedTables = GetCheckedTables();
                if (selectedTables.Any())
                {
                    var dataScript = await _dbCompareService.GenerateDataDiffAsync(selectedTables);
                    if (string.IsNullOrWhiteSpace(dataScript) || (!dataScript.Contains("INSERT INTO") && !dataScript.Contains("UPDATE ") && !dataScript.Contains("DELETE FROM")))
                    {
                        txtExecuteLog.AppendText("-> Data Verification Passed! Selected tables are synchronized.\r\n");
                    }
                    else
                    {
                        txtExecuteLog.AppendText("-> Data Verification FAILED! There are still data differences. Please check Tab 3.\r\n");
                    }
                }
            }
            catch (Exception ex)
            {
                txtExecuteLog.AppendText($"Verification Error: {ex.Message}\r\n");
            }
        }

        private async Task LoadJunkDataAsync(string keyword)
        {
            if (!EnsureServicesInitialized()) return;
            if (string.IsNullOrWhiteSpace(keyword)) return;

            dgvJunkData.Columns.Clear();
            dgvJunkData.Rows.Clear();
            dgvJunkData.Columns.Add("TableName", "Table Name");
            dgvJunkData.Columns.Add("PrimaryKeyColumn", "PK Column");
            dgvJunkData.Columns.Add("PrimaryKeyValue", "PK Value");
            dgvJunkData.Columns.Add("ColumnName", "Column containing Junk");
            dgvJunkData.Columns.Add("DetectedContent", "Detected Content");

            try
            {
                var records = await _oldPgService.SearchJunkDataAsync(keyword);
                foreach (var r in records)
                {
                    dgvJunkData.Rows.Add(r.TableName ?? "", r.PrimaryKeyColumn ?? "", r.PrimaryKeyValue ?? "", r.ColumnName ?? "", r.DetectedContent ?? "");
                }
                MessageBox.Show($"Found {records.Count} suspected junk records.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading junk data: {ex.Message}");
            }
        }

        private async void BtnDeleteJunkData_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            int deleted = 0;
            try
            {
                foreach (DataGridViewRow row in dgvJunkData.SelectedRows)
                {
                    var table = row.Cells["TableName"].Value?.ToString();
                    var pkCol = row.Cells["PrimaryKeyColumn"].Value?.ToString();
                    var pkVal = row.Cells["PrimaryKeyValue"].Value?.ToString();

                    if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(pkCol) || string.IsNullOrEmpty(pkVal)) continue;

                    await _oldPgService!.DeleteRecordAsync(table, pkCol, pkVal);
                    dgvJunkData.Rows.Remove(row);
                    deleted++;
                }
                MessageBox.Show($"Successfully deleted {deleted} rows.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting data: {ex.Message}");
            }
        }

        private void SelectFile(TextBox txt, string filter)
        {
            using (var ofd = new OpenFileDialog { Filter = filter })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txt.Text = ofd.FileName;
                }
            }
        }

        private void BtnCompareConfig_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            if (!File.Exists(txtOldConfigPath.Text) || !File.Exists(txtNewConfigPath.Text)) return;

            try
            {
                var cmpService = new ConfigCompareService();
                string diffOutput, cleanContent;
                bool hasChanges;

                if (txtOldConfigPath.Text.EndsWith(".json"))
                {
                    diffOutput = cmpService.CompareJsonFiles(txtOldConfigPath.Text, txtNewConfigPath.Text, out hasChanges, out cleanContent);
                }
                else
                {
                    diffOutput = cmpService.CompareEnvFiles(txtOldConfigPath.Text, txtNewConfigPath.Text, out hasChanges, out cleanContent);
                }

                txtConfigDiffLog.Text = diffOutput;

                if (hasChanges && _fileSystemService != null)
                {
                    var notePath = _fileSystemService.GetNoteFilePath();
                    var header = $"\r\n=== Changes for {Path.GetFileName(txtNewConfigPath.Text)} ===\r\n";
                    _fileSystemService.AppendToFile(notePath, header + diffOutput);
                    
                    var cleanPath = Path.Combine(_fileSystemService.BaseReleasePath, "source_code", "clean_" + Path.GetFileName(txtNewConfigPath.Text));
                    _fileSystemService.WriteToFile(cleanPath, cleanContent);
                    
                    txtConfigDiffLog.AppendText($"\r\nNote generated at {notePath}\r\nClean config generated at {cleanPath}");
                }
            }
            catch (Exception ex)
            {
                txtConfigDiffLog.AppendText($"Error: {ex.Message}");
            }
        }

        private async void BtnReviewSchema_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized() || _fileSystemService == null) return;
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, true);
            if (!File.Exists(path)) { txtAiReviewLog.Text = "Schema script not generated yet."; return; }

            txtAiReviewLog.Text = "Sending script to AI for review...\r\n";
            if (_aiService == null) return;
            var result = await _aiService.ReviewSqlScriptAsync(File.ReadAllText(path), $"Release from {OldDbName} to {NewDbName}");
            txtAiReviewLog.Text = result;
        }

        private async void BtnReviewConfig_Click(object sender, EventArgs e)
        {
            if (!EnsureServicesInitialized() || _aiService == null) return;
            if (string.IsNullOrEmpty(txtConfigDiffLog.Text)) { txtAiReviewLog.Text = "Please compare config first."; return; }
            
            txtAiReviewLog.Text = "Sending config diff to AI for review...\r\n";
            var result = await _aiService.ReviewConfigChangesAsync(txtConfigDiffLog.Text);
            txtAiReviewLog.Text = result;
        }
        private List<string> GetCheckedTables()
        {
            var selectedTables = new List<string>();
            foreach (TreeNode root in treeSchema.Nodes)
            {
                if (root.Text == "Tables")
                {
                    foreach (TreeNode node in root.Nodes)
                    {
                        if (node.Checked)
                        {
                            var name = node.Text;
                            if (name.Contains("] ")) name = name.Substring(name.IndexOf("] ") + 2);
                            selectedTables.Add(name);
                        }
                    }
                }
            }
            return selectedTables;
        }
    }
}
