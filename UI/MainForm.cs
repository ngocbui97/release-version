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
using System.Diagnostics;

namespace ReleasePrepTool.UI
{
    public partial class MainForm : Form
    {
        private TabControl tabControl = default!;
        
        // Tab 1: Configuration & Setup
        private TextBox txtProductName = null!, txtPgBinPath = null!, txtAiKey = null!, txtReleaseVersion = null!, txtReleasePath = null!;
        private Label lblOldDbStatus = null!, lblNewDbStatus = null!;
        private Label lblSourceSchemaHeader = null!, lblTargetSchemaHeader = null!;
        private GroupBox gbSourceData = null!, gbTargetData = null!;
        private DatabaseConfig _oldDbConfig = null!, _newDbConfig = null!;
        private Button btnConnect = null!;
        
        // Tab 2: Databases Backup
        private Button btnBackupOld = null!;

        // Tab 3: Compare DB
        private SplitContainer splitCompare = null!;
        private TreeView treeSchema = null!;
        private Button btnLoadTables = null!;
        private ComboBox cmbSourceDb = null!, cmbTargetDb = null!;
        private ComboBox cmbSourceSchema = null!, cmbTargetSchema = null!;
        // Data Compare
        private ComboBox cmbSourceDataDb = null!, cmbTargetDataDb = null!;
        private ComboBox cmbSourceDataSchema = null!, cmbTargetDataSchema = null!, cmbJunkSchema = null!;
        private Button btnCompareData = null!;
        private DataGridView dgvTableDiffs = null!;
        // private TextBox txtDbDiffLog; // Moved to combined declaration

        // Tab 4: Sync & Execute DB
        private Button btnExecuteSchema = null!, btnExecuteData = null!;
        private TextBox txtDbDiffLog = null!, txtExecuteLog = null!, txtFinalExportLog = null!, txtBackupLog = null!, txtConfigDiffLog = null!, txtAiReviewLog = null!;
        private RichTextBox txtSourceDdl = null!, txtTargetDdl = null!;
        private TextBox txtIgnoreColumns = null!, txtDataFilter = null!;
        private CheckBox chkUseUpsert = null!;
        private List<SchemaDiffResult> _schemaDiffs = new List<SchemaDiffResult>();
        private Label lblDataStatus = null!;
        private ProgressBar pbDataLoading = null!;
        private Button btnRefreshTables = null!;
        
        // Tab 6: Compare Config
        private TextBox txtOldConfigPath = null!, txtNewConfigPath = null!;
        private Button btnSelectOldConfig = null!, btnSelectNewConfig = null!, btnCompareConfig = null!;    

        // Tab 7: Final Export + Tab 8: AI Review
        private Button btnReviewSchema = null!, btnReviewConfig = null!, btnGenerateSchema = null!;
        private Button btnOpenSchemaFolder = null!, btnOpenDataFolder = null!;
        private ComboBox cmbJunkConnection = null!;
        private TreeView tvJunkSelection = null!;
        private TextBox txtJunkKeywords = null!;
        private TreeView tvJunkResults = null!;
        private TabControl tcJunkResults = null!;
        private DataGridView dgvJunkDataResults = null!;
        private SplitContainer splitJunk = null!;
        private Button btnAnalyzeJunk = null!, btnCleanJunk = null!, btnGenerateJunkScript = null!;
        private JunkAnalysisService? _junkService;
        private List<JunkAnalysisResult> _lastJunkResults = new();
        private DatabaseConfig? _customJunkConfig;
        private PostgresService? _customJunkPgService;
        private string? _lastSchemaExportPath, _lastDataExportPath;

        // Services
        private PostgresService? _oldPgService;
        private PostgresService? _newPgService;
        private DatabaseCompareService? _dbCompareService;
        private FileSystemService? _fileSystemService;
        private AIOperationService? _aiService;
        private bool _suppressComboEvents = false; 
        private bool _isInitializingJunk = false; // Guard for startup Junk Tab population

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
            this.WindowState = FormWindowState.Maximized;
            
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
            UpdateTabContextHeaders();
        }

        private void UpdateTabContextHeaders()
        {
            if (lblSourceSchemaHeader != null && _newDbConfig != null)
                lblSourceSchemaHeader.Text = $"Source DDL (New/Dev DB) — {NewDbName} ({_newDbConfig.Host}:{_newDbConfig.Port})";
            if (lblTargetSchemaHeader != null && _oldDbConfig != null)
                lblTargetSchemaHeader.Text = $"Target DDL (Old/Prod DB) — {OldDbName} ({_oldDbConfig.Host}:{_oldDbConfig.Port})";

            if (gbSourceData != null && _newDbConfig != null)
                gbSourceData.Text = $"Source Database (New/Dev) — {NewDbName} ({_newDbConfig.Host}:{_newDbConfig.Port})";
            if (gbTargetData != null && _oldDbConfig != null)
                gbTargetData.Text = $"Target Database (Old/Prod) — {OldDbName} ({_oldDbConfig.Host}:{_oldDbConfig.Port})";
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

            var pnlActions = new FlowLayoutPanel { Width = 600, Height = 50, Margin = new Padding(0, 10, 0, 0) };
            btnConnect = new Button { Text = "Initialize Release Version", Width = 180, Height = 34, Margin = new Padding(5) };
            btnConnect.Click += BtnConnect_Click;
            
            var btnOpenReleaseFolder = new Button { Text = "📂 Open Output Folder", Width = 180, Height = 34, Margin = new Padding(5) };
            btnOpenReleaseFolder.Click += (s, e) => {
                if (Directory.Exists(txtReleasePath.Text)) Process.Start("explorer.exe", txtReleasePath.Text);
                else MessageBox.Show("Release path does not exist yet.");
            };

            pnlActions.Controls.Add(btnConnect);
            pnlActions.Controls.Add(btnOpenReleaseFolder);
            panelConfig.Controls.Add(pnlActions);
            tabConfig.Controls.Add(panelConfig);

            // 2. Restore DB Tab
            var tabBackup = new TabPage("2. Restore Databases");
            var panelBackup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var pnlRestoreRow = new FlowLayoutPanel { Width = 900, Height = 40 };
            btnBackupOld = new Button { Text = "Restore Old DB from File", Width = 200, Margin = new Padding(5) };
            var lblTargetDb = new Label { Text = "Target DB Name (empty = use connection name):", Width = 280, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(5) };
            var txtTargetDbName = new TextBox { Width = 200, Margin = new Padding(5), PlaceholderText = "e.g. my_old_db_v1" };
            btnBackupOld.Click += (object? s, EventArgs e) => RestoreDbAsync(_oldPgService, OldDbName, txtTargetDbName.Text.Trim());
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

            var pnlDbSelection = new Panel { Dock = DockStyle.Top, Height = 135, Padding = new Padding(10, 10, 10, 0), BackColor = Color.White };
            var lblSrc = new Label { Text = "Source DB", Location = new Point(10, 12), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbSourceDb = new ComboBox { Location = new Point(90, 10), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblSrcSch = new Label { Text = "Schema", Location = new Point(10, 39), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbSourceSchema = new ComboBox { Location = new Point(90, 37), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };

            var lblTgt = new Label { Text = "Target DB", Location = new Point(10, 69), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbTargetDb = new ComboBox { Location = new Point(90, 67), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblTgtSch = new Label { Text = "Schema", Location = new Point(10, 96), Width = 75, Font = new Font(this.Font, FontStyle.Bold) };
            cmbTargetSchema = new ComboBox { Location = new Point(90, 94), Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
            
            var btnRefreshDbs = new Button { Text = "↻ Refresh DBs", Location = new Point(90, 124), Width = 155, Height = 25, BackColor = Color.FromArgb(240, 240, 240) };
            btnRefreshDbs.Click += async (s, e) => await LoadDatabaseListsAsync();
            
            cmbSourceDb.SelectedIndexChanged += async (s, e) => await LoadSchemaListsAsync(cmbSourceDb.Text, cmbSourceSchema, _newDbConfig);
            cmbTargetDb.SelectedIndexChanged += async (s, e) => await LoadSchemaListsAsync(cmbTargetDb.Text, cmbTargetSchema, _oldDbConfig);
            
            pnlDbSelection.Height = 155;
            pnlDbSelection.Controls.AddRange(new Control[] { lblSrc, cmbSourceDb, lblSrcSch, cmbSourceSchema, lblTgt, cmbTargetDb, lblTgtSch, cmbTargetSchema, btnRefreshDbs });

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
            var pnlActionBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5), BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight };
            btnGenerateSchema = new Button { Text = "Export Schema Script", Width = 160, Height = 34, BackColor = Color.FromArgb(240, 240, 240) };
            btnGenerateSchema.Click += BtnGenerateSchema_Click;
            btnOpenSchemaFolder = new Button { Text = "📂", Width = 40, Height = 34, BackColor = Color.White, Visible = false };
            btnOpenSchemaFolder.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastSchemaExportPath) && File.Exists(_lastSchemaExportPath)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSchemaExportPath}\"") { UseShellExecute = true }); };
            
            txtDbDiffLog = new TextBox { Width = 500, Height = 34, BorderStyle = BorderStyle.None, ReadOnly = true, BackColor = Color.White, Multiline = true, Font = new Font(this.Font.FontFamily, 8f), Margin = new Padding(10, 8, 0, 0) };
            pnlActionBar.Controls.Add(btnGenerateSchema);
            pnlActionBar.Controls.Add(btnOpenSchemaFolder);
            pnlActionBar.Controls.Add(txtDbDiffLog);

            var pnlHeaders = new TableLayoutPanel { Dock = DockStyle.Top, Height = 28, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(240, 240, 240) };
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            lblSourceSchemaHeader = new Label { Text = "Source DDL (New/Dev DB)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10,0,0,0) };
            lblTargetSchemaHeader = new Label { Text = "Target DDL (Old/Prod DB)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10,0,0,0) };
            pnlHeaders.Controls.Add(lblSourceSchemaHeader, 0, 0);
            pnlHeaders.Controls.Add(lblTargetSchemaHeader, 1, 0);

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
 
            gbSourceData = new GroupBox { Text = "Source Database (New/Dev)", Dock = DockStyle.Fill, Padding = new Padding(10, 15, 10, 10) };
            var pnlSourceData = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            pnlSourceData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlSourceData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            
            cmbSourceDataDb = new ComboBox { Name = "cmbSourceDataDb", Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSourceDataSchema = new ComboBox { Name = "cmbSourceDataSchema", Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSourceDataDb.SelectedIndexChanged += async (s, e) => {
                await LoadSchemaListsAsync(cmbSourceDataDb.Text, cmbSourceDataSchema, _newDbConfig);
                if (!_suppressComboEvents && !string.IsNullOrEmpty(cmbSourceDataDb.Text) && !string.IsNullOrEmpty(cmbTargetDataDb.Text))
                    BtnLoadDataTables_Click(null!, null!);
            };
            
            pnlSourceData.Controls.Add(new Label { Text = "Database:", AutoSize = true }, 0, 0);
            pnlSourceData.Controls.Add(cmbSourceDataDb, 0, 1);
            pnlSourceData.Controls.Add(new Label { Text = "Schema:", AutoSize = true }, 1, 0);
            pnlSourceData.Controls.Add(cmbSourceDataSchema, 1, 1);
            gbSourceData.Controls.Add(pnlSourceData);
            pnlDataSetup.Controls.Add(gbSourceData, 0, 0);
 
            var lblArrow = new Label { Text = "↔", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24f), ForeColor = Color.Gray };
            pnlDataSetup.Controls.Add(lblArrow, 1, 0);
 
            gbTargetData = new GroupBox { Text = "Target Database (Old/Prod)", Dock = DockStyle.Fill, Padding = new Padding(10, 15, 10, 10) };
            var pnlTargetData = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            pnlTargetData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlTargetData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            
            cmbTargetDataDb = new ComboBox { Name = "cmbTargetDataDb", Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTargetDataSchema = new ComboBox { Name = "cmbTargetDataSchema", Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTargetDataDb.SelectedIndexChanged += async (s, e) => {
                await LoadSchemaListsAsync(cmbTargetDataDb.Text, cmbTargetDataSchema, _oldDbConfig);
                if (!_suppressComboEvents && !string.IsNullOrEmpty(cmbSourceDataDb.Text) && !string.IsNullOrEmpty(cmbTargetDataDb.Text))
                    BtnLoadDataTables_Click(null!, null!);
            };
            
            pnlTargetData.Controls.Add(new Label { Text = "Database:", AutoSize = true }, 0, 0);
            pnlTargetData.Controls.Add(cmbTargetDataDb, 0, 1);
            pnlTargetData.Controls.Add(new Label { Text = "Schema:", AutoSize = true }, 1, 0);
            pnlTargetData.Controls.Add(cmbTargetDataSchema, 1, 1);
            gbTargetData.Controls.Add(pnlTargetData);
            pnlDataSetup.Controls.Add(gbTargetData, 2, 0);

            var gbDataOptions = new GroupBox { Text = "Comparison Options", Dock = DockStyle.Top, Height = 65, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(0, 5, 0, 0) };
            var pnlDataOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            
            pnlDataOptions.Controls.Add(new Label { Text = "Ignore Columns (csv):", AutoSize = true, Margin = new Padding(0, 5, 0, 0) });
            txtIgnoreColumns = new TextBox { Width = 150, Margin = new Padding(5, 0, 15, 0), PlaceholderText = "e.g. updated_at, created_at" };
            pnlDataOptions.Controls.Add(txtIgnoreColumns);
            
            pnlDataOptions.Controls.Add(new Label { Text = "Filter (WHERE):", AutoSize = true, Margin = new Padding(0, 5, 0, 0) });
            txtDataFilter = new TextBox { Width = 200, Margin = new Padding(5, 0, 15, 0), PlaceholderText = "e.g. id > 1000" };
            pnlDataOptions.Controls.Add(txtDataFilter);
            
            chkUseUpsert = new CheckBox { Text = "Use UPSERT (ON CONFLICT)", AutoSize = true, Margin = new Padding(0, 4, 15, 0), Checked = true };
            pnlDataOptions.Controls.Add(chkUseUpsert);
            
            gbDataOptions.Controls.Add(pnlDataOptions);
            pnlDataMain.Controls.Add(gbDataOptions);
            pnlDataMain.Controls.SetChildIndex(gbDataOptions, 1);
            pnlDataMain.Controls.Add(pnlDataSetup);
            pnlDataMain.Controls.SetChildIndex(pnlDataSetup, 0);

            var pnlDataActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 8, 0, 0) };

            btnCompareData = new Button { Name = "btnCompareData", Text = "▶ Start Comparison", Width = 170, Height = 34, Font = new Font(this.Font, FontStyle.Bold), BackColor = Color.AliceBlue };
            btnCompareData.Click += BtnCompareData_Click;
            pnlDataActions.Controls.Add(btnCompareData);

            btnRefreshTables = new Button { Text = "🔄 Load Tables", Width = 140, Height = 34, BackColor = Color.WhiteSmoke };
            btnRefreshTables.Click += (s, e) => BtnLoadDataTables_Click(null!, null!);
            pnlDataActions.Controls.Add(btnRefreshTables);

            var btnGenerateData = new Button { Text = "Export Sync Script", Width = 150, Height = 34 };
            btnGenerateData.Click += BtnGenerateData_Click;
            pnlDataActions.Controls.Add(btnGenerateData);

            btnOpenDataFolder = new Button { Text = "📂", Width = 40, Height = 34, BackColor = Color.White, Visible = false };
            btnOpenDataFolder.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastDataExportPath) && File.Exists(_lastDataExportPath)) Process.Start("explorer.exe", $"/select,\"{_lastDataExportPath}\""); };
            pnlDataActions.Controls.Add(btnOpenDataFolder);

            var btnSelectAllData = new Button { Text = "✔ Select All", Width = 110, Height = 34 };
            btnSelectAllData.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in dgvTableDiffs.Rows)
                    if (row.Visible && !row.Cells["ColCheck"].ReadOnly)
                        row.Cells["ColCheck"].Value = true;
            };
            pnlDataActions.Controls.Add(btnSelectAllData);

            var btnSelectNoneData = new Button { Text = "✘ Select None", Width = 110, Height = 34 };
            btnSelectNoneData.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in dgvTableDiffs.Rows)
                    if (row.Visible)
                        row.Cells["ColCheck"].Value = false;
            };
            pnlDataActions.Controls.Add(btnSelectNoneData);

            // Filter dropdown
            var lblFilter = new Label { Text = "Filter:", Width = 50, Height = 34, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(8, 0, 2, 0) };
            pnlDataActions.Controls.Add(lblFilter);
            var cmbFilter = new ComboBox { Width = 160, Height = 34, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 5, 0, 0) };
            cmbFilter.Items.AddRange(new object[] { "All Tables", "Selected (Checked)", "Unselected", "Different", "Synchronized", "Added (New)", "Removed (Old)", "⚠️ No PK" });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += (s, e) => ApplyTableFilter(cmbFilter.SelectedItem?.ToString() ?? "All Tables");
            pnlDataActions.Controls.Add(cmbFilter);

            lblDataStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(10, 10, 0, 0), Font = new Font(this.Font, FontStyle.Italic), ForeColor = Color.Blue };
            pnlDataActions.Controls.Add(lblDataStatus);

            pbDataLoading = new ProgressBar { Width = 150, Height = 20, Style = ProgressBarStyle.Marquee, Visible = false, Margin = new Padding(10, 15, 0, 0) };
            pnlDataActions.Controls.Add(pbDataLoading);

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
                Margin = new Padding(0, 10, 0, 0),
                RowTemplate = { Height = 28 }
            };

            var chkCol = new DataGridViewCheckBoxColumn {
                Name = "ColCheck",
                HeaderText = "✓",
                Width = 40,
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FillWeight = 1,
                TrueValue = true,
                FalseValue = false
            };
            dgvTableDiffs.Columns.Add(chkCol);
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColName",    HeaderText = "Table Name",        FillWeight = 40 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDiff",    HeaderText = "🔄 Changed",        FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColSource",  HeaderText = "➕ Added (Source)",  FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTarget",  HeaderText = "➖ Removed (Target)",FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColIdentical",HeaderText = "Status",           FillWeight = 15 });

            // Single-click checkbox toggle
            dgvTableDiffs.CellContentClick += (s, ev) => {
                if (dgvTableDiffs.Columns["ColCheck"] != null && ev.ColumnIndex == dgvTableDiffs.Columns["ColCheck"].Index && ev.RowIndex >= 0)
                    dgvTableDiffs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            dgvTableDiffs.CellDoubleClick += DgvTableDiffs_CellDoubleClick;


            pnlDataMain.Controls.Add(dgvTableDiffs);
            pnlDataMain.Controls.Add(pnlDataActions);
            pnlDataMain.Controls.Add(pnlDataSetup);
            dgvTableDiffs.BringToFront();

            var lblHint = new Label { Text = "💡 Double-click a compared row to view detailed record differences.", Dock = DockStyle.Bottom, Height = 25, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font, FontStyle.Italic) };
            pnlDataMain.Controls.Add(lblHint);
            lblHint.SendToBack();

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

            // 6. Clean Junk Tab (Redesigned)
            var tabCleanJunk = new TabPage("6. Clean Junk");
            // Top Panel: Configuration (Target selection, Keywords, and DB/Schema Tree)
            var pnlJunkConfig = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Padding = new Padding(1) };
            pnlJunkConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f)); // Connection & Keywords
            pnlJunkConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f)); // Selection Tree
            pnlJunkConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f)); // Action Buttons
            pnlJunkConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Group 1: Connection & Keywords
            var gbJunkBasics = new GroupBox { Text = "1. Setup & Patterns", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 212), Padding = new Padding(4, 2, 4, 3) };
            var pnlJunkBasics = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 0, 0, 4) };
            
            pnlJunkBasics.Controls.Add(new Label { Text = "Connection Source:", AutoSize = true, Font = new Font(this.Font, FontStyle.Regular), ForeColor = Color.Black, Margin = new Padding(0, 0, 0, 0) });
            cmbJunkConnection = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 2) };
            cmbJunkConnection.Items.AddRange(new string[] { "Source (Dev)", "Target (Prod)", "Custom Connection..." });
            pnlJunkBasics.Controls.Add(cmbJunkConnection);
            
            pnlJunkBasics.Controls.Add(new Label { Text = "Junk Keywords (CSV):", AutoSize = true, Font = new Font(this.Font, FontStyle.Regular), ForeColor = Color.Black, Margin = new Padding(0, 0, 0, 0) });
            txtJunkKeywords = new TextBox { Width = 250, Text = "test, dev, tmp, 123", Margin = new Padding(0, 0, 0, 4) };
            pnlJunkBasics.Controls.Add(txtJunkKeywords);
            
            btnAnalyzeJunk = new Button { Text = "🔍 ANALYZE JUNK", Width = 250, Height = 28, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnAnalyzeJunk.FlatAppearance.BorderSize = 0;
            btnAnalyzeJunk.Click += BtnAnalyzeJunk_Click;
            pnlJunkBasics.Controls.Add(btnAnalyzeJunk);
            gbJunkBasics.Controls.Add(pnlJunkBasics);

            // Group 2: DB -> Schema Tree Selection
            var pnlJunkSelection = new GroupBox { Text = "2. Selection Scope", Dock = DockStyle.Fill, Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 212), Padding = new Padding(4, 2, 4, 3) };
            var pnlJunkSelectionInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 2) };
            var pnlJunkSelectionLinks = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 22, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 1, 0, 0) };
            var lnkSelectAllJunk = new LinkLabel { Text = "Select All", AutoSize = true, Margin = new Padding(4, 2, 10, 0), LinkColor = Color.FromArgb(0, 120, 212), ActiveLinkColor = Color.FromArgb(16, 124, 16), Font = new Font(this.Font, FontStyle.Regular) };
            var lnkUnselectAllJunk = new LinkLabel { Text = "Unselect All", AutoSize = true, Margin = new Padding(0, 2, 0, 0), LinkColor = Color.FromArgb(167, 167, 167), ActiveLinkColor = Color.FromArgb(216, 59, 1), Font = new Font(this.Font, FontStyle.Regular) };
            
            lnkSelectAllJunk.LinkClicked += (s, e) => SetAllJunkSelectionCheckState(true);
            lnkUnselectAllJunk.LinkClicked += (s, e) => SetAllJunkSelectionCheckState(false);
            
            pnlJunkSelectionLinks.Controls.Add(lnkSelectAllJunk);
            pnlJunkSelectionLinks.Controls.Add(lnkUnselectAllJunk);
            
            tvJunkSelection = new TreeView { 
                Dock = DockStyle.Fill, 
                CheckBoxes = true, 
                Font = new Font("Segoe UI", 9f, FontStyle.Regular), 
                BorderStyle = BorderStyle.None,
                Indent = 20,
                ShowLines = true,
                FullRowSelect = false
            };
            tvJunkSelection.AfterCheck += TvJunkSelection_AfterCheck;
            tvJunkSelection.BeforeExpand += TvJunkSelection_BeforeExpand;
            
            pnlJunkSelectionInner.Controls.Add(tvJunkSelection);
            pnlJunkSelectionInner.Controls.Add(pnlJunkSelectionLinks);
            pnlJunkSelection.Controls.Add(pnlJunkSelectionInner);

            // Group 3: Actions
            var gbJunkActions = new GroupBox { Text = "3. Cleanup Actions", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = new Font(this.Font, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 212), Padding = new Padding(4, 2, 4, 3) };
            var pnlJunkGlobalActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 0, 0, 4) };
            
            btnGenerateJunkScript = new Button { Text = "📜 PREVIEW SCRIPT", Width = 250, Height = 28, Margin = new Padding(0, 2, 0, 6), BackColor = Color.FromArgb(243, 242, 241), ForeColor = Color.FromArgb(50, 49, 48), FlatStyle = FlatStyle.Flat, Font = new Font(this.Font, FontStyle.Bold), Cursor = Cursors.Hand };
            btnGenerateJunkScript.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnGenerateJunkScript.Click += BtnGenerateJunkScript_Click;
            
            btnCleanJunk = new Button { Text = "🗑 CLEAN NOW!", Width = 250, Height = 32, BackColor = Color.FromArgb(216, 59, 1), ForeColor = Color.White, Font = new Font(this.Font.FontFamily, 10.5f, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCleanJunk.FlatAppearance.BorderSize = 0;
            btnCleanJunk.Click += BtnCleanJunk_Click;
            
            pnlJunkGlobalActions.Controls.Add(btnGenerateJunkScript);
            pnlJunkGlobalActions.Controls.Add(btnCleanJunk);
            gbJunkActions.Controls.Add(pnlJunkGlobalActions);

            pnlJunkConfig.Controls.Add(gbJunkBasics, 0, 0);
            pnlJunkConfig.Controls.Add(pnlJunkSelection, 1, 0);
            pnlJunkConfig.Controls.Add(gbJunkActions, 2, 0);
            
            // Bottom Panel: Results categorized by Tab (Structure vs Data Records)
            tcJunkResults = new TabControl { Dock = DockStyle.Fill };
            var tabStruct = new TabPage("1. Structure Cleanup");
            var tabData = new TabPage("2. Data Records Cleanup");
            
            tvJunkResults = new TreeView { 
                Dock = DockStyle.Fill, 
                CheckBoxes = true, 
                Font = new Font("Consolas", 9f), 
                BorderStyle = BorderStyle.None,
                DrawMode = TreeViewDrawMode.OwnerDrawText
            };
            tvJunkResults.DrawNode += TvJunkResults_DrawNode;
            tvJunkResults.AfterCheck += TvJunkResults_AfterCheck;
            tvJunkResults.NodeMouseDoubleClick += TvJunkResults_NodeMouseDoubleClick;
            tabStruct.Controls.Add(tvJunkResults);
            
            dgvJunkDataResults = new DataGridView { 
                Dock = DockStyle.Fill, 
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, 
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 8.5f)
            };
            // Add Checkbox column
            var chkColJunk = new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 30, FillWeight = 30 };
            dgvJunkDataResults.Columns.Add(chkColJunk);
            dgvJunkDataResults.Columns.Add("Database", "Database");
            dgvJunkDataResults.Columns.Add("Table", "Table");
            dgvJunkDataResults.Columns.Add("Column", "Column");
            dgvJunkDataResults.Columns.Add("PK", "PK");
            dgvJunkDataResults.Columns.Add("Reason", "Reason/Value");
            
            dgvJunkDataResults.CellDoubleClick += DgvJunkDataResults_CellDoubleClick;
            dgvJunkDataResults.CellPainting += DgvJunkDataResults_CellPainting;
            tabData.Controls.Add(dgvJunkDataResults);
            
            tcJunkResults.TabPages.Add(tabStruct);
            tcJunkResults.TabPages.Add(tabData);
            
            tabCleanJunk.Controls.Add(tcJunkResults);
            tabCleanJunk.Controls.Add(pnlJunkConfig);
            tcJunkResults.BringToFront();

            // Logic to populate when connection changes
            cmbJunkConnection.SelectedIndexChanged += async (s, e) => {
                if (cmbJunkConnection.SelectedIndex == 2) // Custom
                {
                    using (var dlg = new ConnectionDialog("Custom Database Connection", _customJunkConfig))
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _customJunkConfig = dlg.Config;
                            _customJunkPgService = new PostgresService(_customJunkConfig) { PostgresBinPath = txtPgBinPath.Text };
                            await UpdateJunkSelectionTreeAsync();
                        }
                    }
                }
                else
                {
                    await UpdateJunkSelectionTreeAsync(_isInitializingJunk);
                }
            };
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
                if ((tabControl.SelectedTab == tabCompareSchema || tabControl.SelectedTab == tabCompareData) && cmbSourceDb.Items.Count == 0)
                    await LoadDatabaseListsAsync();
            };
            
            this.FormClosing += (s, e) => SaveConfig();
            this.Load += async (s, e) => {
                LoadConfig();
                
                // Pre-load database lists for both comparison tabs if config is present
                if (_oldDbConfig != null && _newDbConfig != null) {
                    await LoadDatabaseListsAsync();
                }

                // Trigger initial Junk tree load AFTER config is loaded
                _isInitializingJunk = true;
                try { cmbJunkConnection.SelectedIndex = 1; } finally { _isInitializingJunk = false; }

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

        private bool EnsureServicesInitialized(bool silent = false)
        {
            if (_oldPgService != null && _newPgService != null && _dbCompareService != null && _fileSystemService != null && _junkService != null)
                return true;

            if (_oldDbConfig == null || _newDbConfig == null)
            {
                if (!silent) MessageBox.Show("Please select connections for both Old and New databases first.");
                return false;
            }

            try
            {
                _oldPgService = new PostgresService(_oldDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _newPgService = new PostgresService(_newDbConfig!) { PostgresBinPath = txtPgBinPath.Text };
                _dbCompareService = new DatabaseCompareService(_oldDbConfig!, _newDbConfig!);
                _fileSystemService = new FileSystemService(txtReleasePath.Text, txtReleaseVersion.Text, txtProductName.Text);
                _aiService = new AIOperationService(txtAiKey.Text);
                _junkService = new JunkAnalysisService(_oldPgService);

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

        private async void BtnConnect_Click(object? sender, EventArgs e)
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

        private async void RestoreDbAsync(PostgresService? service, string dbName, string targetDbName = "")
        {
            if (!EnsureServicesInitialized()) return;
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
                            if (txtBackupLog != null && txtBackupLog.InvokeRequired)
                                txtBackupLog.Invoke(() => txtBackupLog.AppendText(line + "\r\n"));
                            else
                                txtBackupLog?.AppendText(line + "\r\n");
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


        private void TreeSchema_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node == null || e.Node.Parent == null) return;
            var diff = e.Node.Tag as SchemaDiffResult;
            if (diff != null) {
                UpdateDiffView(diff.SourceDDL, diff.TargetDDL);
            } else {
                txtSourceDdl.Clear();
                txtTargetDdl.Clear();
                txtSourceDdl.AppendText("-- Identical/No changes detected.");
                txtTargetDdl.AppendText("-- Identical/No changes detected.");
            }
        }

        private bool IsLikelyChange(string? s, string? t)
        {
            if (s == null || t == null) return false;
            string s1 = s.Trim().TrimEnd(',');
            string t1 = t.Trim().TrimEnd(',');
            if (s1 == t1) return true;

            // Check if both start with the same quoted identifier (column name)
            if (s1.StartsWith("\"") && t1.StartsWith("\""))
            {
                int sIdx = s1.IndexOf("\"", 1);
                int tIdx = t1.IndexOf("\"", 1);
                if (sIdx > 0 && tIdx > 0 && s1.Substring(0, sIdx) == t1.Substring(0, tIdx))
                    return true;
            }
            return false;
        }

        private void UpdateDiffView(string source, string target)
        {
            txtSourceDdl.Clear();
            txtTargetDdl.Clear();

            var sLines = (source ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var tLines = (target ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Compute LCS for alignment
            int m = sLines.Length;
            int n = tLines.Length;
            int[,] lcs = new int[m + 1, n + 1];

            for (int r = 1; r <= m; r++)
            {
                for (int c = 1; c <= n; c++)
                {
                    // Ignore trailing commas for matching logic
                    if (sLines[r - 1].Trim().TrimEnd(',') == tLines[c - 1].Trim().TrimEnd(','))
                        lcs[r, c] = lcs[r - 1, c - 1] + 1;
                    else
                        lcs[r, c] = Math.Max(lcs[r - 1, c], lcs[r, c - 1]);
                }
            }

            var diffRows = new List<(string? s, string? t, Color sCol, Color tCol, bool isPair)>();
            int i = m, j = n;
            while (i > 0 || j > 0)
            {
                if (i > 0 && j > 0 && sLines[i - 1].Trim().TrimEnd(',') == tLines[j - 1].Trim().TrimEnd(','))
                {
                    diffRows.Add((sLines[i - 1], tLines[j - 1], Color.White, Color.White, false));
                    i--; j--;
                }
                else if (i > 0 && j > 0 && IsLikelyChange(sLines[i - 1], tLines[j - 1]))
                {
                    // Light Blue (230, 240, 255) for paired lines
                    var modCol = Color.FromArgb(230, 240, 255);
                    diffRows.Add((sLines[i - 1], tLines[j - 1], modCol, modCol, true));
                    i--; j--;
                }
                else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
                {
                    diffRows.Add((null, tLines[j - 1], Color.White, Color.FromArgb(220, 255, 220), false));
                    j--;
                }
                else
                {
                    diffRows.Add((sLines[i - 1], null, Color.FromArgb(255, 220, 220), Color.White, false));
                    i--;
                }
            }
            diffRows.Reverse();

            foreach (var row in diffRows)
            {
                AppendDiffLine(txtSourceDdl, row.s, row.sCol, row.isPair ? row.t : null);
                AppendDiffLine(txtTargetDdl, row.t, row.tCol, row.isPair ? row.s : null);
            }
        }

        private void AppendDiffLine(RichTextBox rtb, string? text, Color backColor, string? otherText = null)
        {
            int start = rtb.TextLength;
            string displayText = (text ?? "");
            rtb.AppendText(displayText + "\n");
            int end = rtb.TextLength;
            rtb.Select(start, end - start);
            rtb.SelectionBackColor = backColor;
            
            if (backColor != Color.White)
            {
                // For paired (modified) lines, highlight the word-level difference
                if (otherText != null && text != null && backColor == Color.FromArgb(230, 240, 255))
                {
                    HighlightInLineDiff(rtb, start, text, otherText);
                }
                else
                {
                    rtb.SelectionColor = Color.FromArgb(40, 40, 60);
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                }
            }
            rtb.DeselectAll();
        }

        private void HighlightInLineDiff(RichTextBox rtb, int lineStart, string s1, string s2)
        {
            // Find common prefix
            int prefixLen = 0;
            while (prefixLen < s1.Length && prefixLen < s2.Length && s1[prefixLen] == s2[prefixLen])
                prefixLen++;

            // Find common suffix
            int suffixLen = 0;
            while (suffixLen < s1.Length - prefixLen && suffixLen < s2.Length - prefixLen && 
                   s1[s1.Length - 1 - suffixLen] == s2[s2.Length - 1 - suffixLen])
                suffixLen++;

            // Highlight the divergent section
            int diffStart = lineStart + prefixLen;
            int diffLen = s1.Length - prefixLen - suffixLen;

            if (diffLen > 0)
            {
                rtb.Select(diffStart, diffLen);
                rtb.SelectionColor = Color.Blue; // Contrast blue for change
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            }
        }



        private void TreeSchema_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (e.Node == null) return;
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
                case "ExistingInTarget": node.ForeColor = Color.Red; node.Text = "[REMOVED] " + node.Text; break;
            }
        }

        private async void BtnLoadTables_Click(object sender, EventArgs e)
        {
            if (cmbSourceDb.SelectedItem == null || cmbTargetDb.SelectedItem == null) {
                MessageBox.Show("Please select both Source and Target databases from the list.");
                return;
            }

            // Update configs with selected database names
            // Standardizing: Left Side = Source (New/Dev), Right Side = Target (Old/Prod)
            _newDbConfig!.DatabaseName = cmbSourceDb.SelectedItem?.ToString() ?? ""; 
            _oldDbConfig!.DatabaseName = cmbTargetDb.SelectedItem?.ToString() ?? ""; 
            
            // Force re-initialization of services for the new databases
            _oldPgService = null;
            _newPgService = null;
            _dbCompareService = null;

            if (!EnsureServicesInitialized()) return;
            _dbCompareService = new DatabaseCompareService(_newDbConfig!, _oldDbConfig!); // Passing New then Old
            treeSchema.Nodes.Clear();
            btnLoadTables.Text = "Loading...";
            btnLoadTables.Enabled = false;
            try
            {
                string sourceSchema = cmbSourceSchema.Text;
                string targetSchema = cmbTargetSchema.Text;
                if (string.IsNullOrEmpty(sourceSchema) || string.IsNullOrEmpty(targetSchema))
                {
                    MessageBox.Show("Please select both source and target schemas first.");
                    btnLoadTables.Text = "Load Diffs";
                    btnLoadTables.Enabled = true;
                    return;
                }

                txtDbDiffLog.Text = $"Generating schema diffs ('{sourceSchema}' vs '{targetSchema}')...";
                _schemaDiffs = await _dbCompareService!.GenerateSchemaDiffResultsAsync(sourceSchema, targetSchema);

                var oldTables = await _oldPgService!.GetSchemaTablesAsync(_oldDbConfig!.DatabaseName!, targetSchema);
                var newTables = await _newPgService!.GetSchemaTablesAsync(_newDbConfig!.DatabaseName!, sourceSchema);
                var allTables = oldTables.Union(newTables).OrderBy(t => t).ToList();
                // var commonTables = oldTables.Intersect(newTables).OrderBy(t => t).ToList(); // Not needed locally now

                var tableRoot = new TreeNode("Tables");
                var viewRoot = new TreeNode("Views");
                var routineRoot = new TreeNode("Functions");
                var triggerRoot = new TreeNode("Triggers");
                var constraintRoot = new TreeNode("Constraints");
                var indexNodeRoot = new TreeNode("Indexes");
                var extensionRoot = new TreeNode("Extensions");
                var roleRoot = new TreeNode("Roles");
                var sequenceRoot = new TreeNode("Sequences");
                var enumRoot = new TreeNode("Enums");
                var matViewRoot = new TreeNode("Materialized Views");

                foreach (var table in allTables)
                {
                    var diff = _schemaDiffs.FirstOrDefault(d => d.ObjectName == table && d.ObjectType == "Table");
                    if (diff == null) continue; // Only show if there's a difference

                    var node = new TreeNode(table) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    tableRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "View"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    viewRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Routine"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    routineRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Index"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    indexNodeRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Trigger"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    triggerRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Constraint"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    constraintRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Extension"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    extensionRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Role"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    roleRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Sequence"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    sequenceRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Enum"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    enumRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Materialized View"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    matViewRoot.Nodes.Add(node);
                }

                if (tableRoot.Nodes.Count > 0) treeSchema.Nodes.Add(tableRoot);
                if (viewRoot.Nodes.Count > 0) treeSchema.Nodes.Add(viewRoot);
                if (routineRoot.Nodes.Count > 0) treeSchema.Nodes.Add(routineRoot);
                if (indexNodeRoot.Nodes.Count > 0) treeSchema.Nodes.Add(indexNodeRoot);
                if (triggerRoot.Nodes.Count > 0) treeSchema.Nodes.Add(triggerRoot);
                if (constraintRoot.Nodes.Count > 0) treeSchema.Nodes.Add(constraintRoot);
                if (extensionRoot.Nodes.Count > 0) treeSchema.Nodes.Add(extensionRoot);
                if (roleRoot.Nodes.Count > 0) treeSchema.Nodes.Add(roleRoot);
                if (sequenceRoot.Nodes.Count > 0) treeSchema.Nodes.Add(sequenceRoot);
                if (enumRoot.Nodes.Count > 0) treeSchema.Nodes.Add(enumRoot);
                if (matViewRoot.Nodes.Count > 0) treeSchema.Nodes.Add(matViewRoot);
                
                tableRoot.Expand();

                var summary = new StringBuilder($"Found {_schemaDiffs.Count} schema differences: ");
                var typeCounts = _schemaDiffs.GroupBy(d => d.ObjectType)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Count()} {g.Key}{(g.Count() > 1 ? (g.Key == "Index" ? "es" : "s") : "")}")
                    .ToList();
                summary.Append(string.Join(", ", typeCounts));
                txtDbDiffLog.Text = summary.ToString();
            }
            catch (Exception ex)
            {
                txtDbDiffLog.Text = $"Error: {ex.ToString()}";
            }
            finally
            {
                btnLoadTables.Text = "Load Diffs";
                btnLoadTables.Enabled = true;
            }
        }

        private async void BtnGenerateSchema_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            string sourceSchema = cmbSourceSchema.Text;
            string targetSchema = cmbTargetSchema.Text;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"-- Schema update script from {sourceSchema} to {targetSchema}");
                sb.AppendLine($"-- Source: {cmbSourceDb.Text}, Target: {cmbTargetDb.Text}");
                sb.AppendLine($"-- Generated at {DateTime.Now}\n");

                foreach (var r in _schemaDiffs)
                {
                    sb.AppendLine($"-- {r.ObjectType}: {r.ObjectName} ({r.DiffType})");
                    sb.AppendLine(r.DiffScript);
                    sb.AppendLine();
                }

                var sql = sb.ToString();
                var path = _fileSystemService!.GetSqlScriptPath(NewDbName, true);
                _fileSystemService!.WriteToFile(path, sql);
                txtDbDiffLog.Text = $"Schema script exported to {path}";

                _lastSchemaExportPath = path;
                btnOpenSchemaFolder.Visible = true;
                MessageBox.Show($"Schema script exported successfully to:\n{path}\n\nClick the 📂 button to open the folder.", "Export Successful");
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
                        txtProductName.Text = (string?)config.Product ?? txtProductName.Text;
                        if (config.OldConfig != null) {
                            var oldCfg = config.OldConfig.ToObject<DatabaseConfig>();
                            if (oldCfg != null) _oldDbConfig = oldCfg;
                        }
                        if (config.NewConfig != null) {
                            var newCfg = config.NewConfig.ToObject<DatabaseConfig>();
                            if (newCfg != null) _newDbConfig = newCfg;
                        }
                        txtPgBinPath.Text = (string?)config.PgBin ?? txtPgBinPath.Text; 
                        txtReleaseVersion.Text = (string?)config.Version ?? txtReleaseVersion.Text;
                        txtReleasePath.Text = (string?)config.ReleasePath ?? txtReleasePath.Text; 
                        txtAiKey.Text = (string?)config.AiKey ?? txtAiKey.Text;
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

            _suppressComboEvents = true;  // Prevent SelectedIndexChanged from triggering BtnLoadDataTables_Click
            try {
                var oldSvc = new PostgresService(_oldDbConfig);
                var newSvc = new PostgresService(_newDbConfig);

                var oldDbs = await oldSvc.GetAllDatabasesAsync();
                var newDbs = await newSvc.GetAllDatabasesAsync();

                cmbSourceDb.Items.Clear();
                cmbSourceDb.Items.AddRange(newDbs.ToArray());
                cmbSourceDataDb.Items.Clear();
                cmbSourceDataDb.Items.AddRange(newDbs.ToArray());

                if (newDbs.Contains(_newDbConfig.DatabaseName)) {
                    cmbSourceDb.SelectedItem = _newDbConfig.DatabaseName;
                    cmbSourceDataDb.SelectedItem = _newDbConfig.DatabaseName;
                }
                else if (newDbs.Any()) {
                    cmbSourceDb.SelectedIndex = 0;
                    cmbSourceDataDb.SelectedIndex = 0;
                }

                cmbTargetDb.Items.Clear();
                cmbTargetDb.Items.AddRange(oldDbs.ToArray());
                cmbTargetDataDb.Items.Clear();
                cmbTargetDataDb.Items.AddRange(oldDbs.ToArray());

                if (oldDbs.Contains(_oldDbConfig.DatabaseName)) {
                    cmbTargetDb.SelectedItem = _oldDbConfig.DatabaseName;
                    cmbTargetDataDb.SelectedItem = _oldDbConfig.DatabaseName;
                }
                else if (oldDbs.Any()) {
                    cmbTargetDb.SelectedIndex = 0;
                    cmbTargetDataDb.SelectedIndex = 0;
                }

                // Initial Schema Load Logic
                if (cmbSourceDb.SelectedItem != null) await LoadSchemaListsAsync(cmbSourceDb.SelectedItem.ToString()!, cmbSourceSchema, _newDbConfig);
                if (cmbTargetDb.SelectedItem != null) await LoadSchemaListsAsync(cmbTargetDb.SelectedItem.ToString()!, cmbTargetSchema, _oldDbConfig);
                if (cmbSourceDataDb.SelectedItem != null) await LoadSchemaListsAsync(cmbSourceDataDb.SelectedItem.ToString()!, cmbSourceDataSchema, _newDbConfig);
                if (cmbTargetDataDb.SelectedItem != null) await LoadSchemaListsAsync(cmbTargetDataDb.SelectedItem.ToString()!, cmbTargetDataSchema, _oldDbConfig);
                if (cmbTargetDataDb.SelectedItem != null) await LoadSchemaListsAsync(cmbTargetDataDb.SelectedItem.ToString()!, cmbJunkSchema, _oldDbConfig);
            }
            catch (Exception ex) {
                MessageBox.Show($"Error loading database lists: {ex.Message}");
            }
            finally {
                _suppressComboEvents = false;  // Re-enable events after all updates are done
            }
        }

        private async Task LoadSchemaListsAsync(string dbName, ComboBox targetCmb, DatabaseConfig? baseConfig)
        {
            if (string.IsNullOrEmpty(dbName) || baseConfig == null) return;
            
            try
            {
                // Temporarily create service to fetch schemas
                var tempCfg = new DatabaseConfig {
                    Host = baseConfig.Host,
                    Port = baseConfig.Port,
                    Username = baseConfig.Username,
                    Password = baseConfig.Password,
                    DatabaseName = dbName
                };
                
                var svc = new DatabaseCompareService(tempCfg, tempCfg);
                var schemas = await svc.GetSchemasAsync(tempCfg);
                
                targetCmb.Items.Clear();
                foreach (var s in schemas) targetCmb.Items.Add(s);
                
                if (targetCmb.Items.Contains("public")) targetCmb.SelectedItem = "public";
                else if (targetCmb.Items.Count > 0) targetCmb.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                txtDbDiffLog.Text = $"Error loading schemas for {dbName}: {ex.Message}";
            }
        }

        private async void BtnLoadDataTables_Click(object? sender, EventArgs e)
        {
            if (cmbSourceDataDb.SelectedItem == null || cmbTargetDataDb.SelectedItem == null) {
                MessageBox.Show("Please select both Source and Target databases.");
                return;
            }

            // Bug#1 fix: Source UI = New/Dev DB, Target UI = Old/Prod DB
            _newDbConfig.DatabaseName = cmbSourceDataDb.SelectedItem.ToString()!;
            _oldDbConfig.DatabaseName = cmbTargetDataDb.SelectedItem.ToString()!;
            _dbCompareService = null; // Force recreation with new DBs

            if (!EnsureServicesInitialized()) return;
            
            string sourceSchema = cmbSourceDataSchema.Text;
            string targetSchema = cmbTargetDataSchema.Text;
            if (string.IsNullOrEmpty(sourceSchema) || string.IsNullOrEmpty(targetSchema)) {
                MessageBox.Show("Please select both source and target schemas.");
                return;
            }

            lblDataStatus.Text = $"⌛ Loading tables from schemas '{sourceSchema}' and '{targetSchema}'...";
            lblDataStatus.ForeColor = Color.Blue;
            pbDataLoading.Visible = true;
            this.Cursor = Cursors.WaitCursor;
            
            try
            {
                var sourceTables = await _newPgService!.GetSchemaTablesAsync(_newDbConfig.DatabaseName, sourceSchema);
                var targetTables = await _oldPgService!.GetSchemaTablesAsync(_oldDbConfig.DatabaseName, targetSchema);
                
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
                        var count = await _newPgService!.GetTableRowCountAsync(table, sourceSchema);
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 240, 255); // Light Blue (Added)
                        row.Cells["ColIdentical"].Value = "Added (New)";
                        row.Cells["ColSource"].Value = count;
                        row.Cells["ColCheck"].Value = false;
                        row.Cells["ColCheck"].ReadOnly = true;
                    }
                    else if (!inSource && inTarget)
                    {
                        var count = await _oldPgService!.GetTableRowCountAsync(table, targetSchema);
                        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 230); // Light Pink (Removed)
                        row.Cells["ColIdentical"].Value = "Removed (Old)";
                        row.Cells["ColTarget"].Value = count;
                        row.Cells["ColCheck"].Value = false;
                        row.Cells["ColCheck"].ReadOnly = true;
                    }
                    else
                    {
                        var pks = await _newPgService!.GetPrimaryKeysAsync(_newDbConfig.DatabaseName, table, sourceSchema);
                        if (!pks.Any())
                        {
                            row.Cells["ColIdentical"].Value = "⚠️ No Primary Key";
                            row.Cells["ColCheck"].Value = false;
                            row.Cells["ColCheck"].ReadOnly = true;
                            row.DefaultCellStyle.ForeColor = Color.Gray;
                        }
                        else
                        {
                            row.Cells["ColIdentical"].Value = "⌛ Waiting Compare";
                        }
                    }
                }

                if (!allTables.Any())
                    lblDataStatus.Text = $"ℹ️ No tables found in schemas.";
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
                pbDataLoading.Visible = false;
                this.Cursor = Cursors.Default;
            }
        }

        private async void BtnCompareData_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            
            // Clear previous results for common tables (make all rows visible first)
            foreach (DataGridViewRow row in dgvTableDiffs.Rows)
            {
                row.Visible = true;
                var statusVal = row.Cells["ColIdentical"].Value?.ToString();
                if (statusVal == "⌛ Waiting Compare" || statusVal == "Different" || statusVal == "Synchronized")
                {
                    row.Cells["ColDiff"].Value = "";
                    row.Cells["ColSource"].Value = "";
                    row.Cells["ColTarget"].Value = "";
                    row.Cells["ColIdentical"].Value = "⌛ Waiting Compare";
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                }
            }

            var checkedRows = dgvTableDiffs.Rows.Cast<DataGridViewRow>()
                .Where(r => Convert.ToBoolean(r.Cells["ColCheck"].Value))
                .ToList();

            if (!checkedRows.Any()) { MessageBox.Show("Please select tables to compare."); return; }

            btnCompareData.Enabled = false;
            btnCompareData.Text = "⏳ Comparing...";
            lblDataStatus.Text = "⌛ Comparing table data...";
            pbDataLoading.Visible = true;
            this.Cursor = Cursors.WaitCursor;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int diffCount = 0, syncCount = 0;

            try
            {
                foreach (var row in checkedRows)
                {
                    string table = row.Cells["ColName"].Value?.ToString() ?? "";
                    string sourceSchema = cmbSourceDataSchema.Text;
                    string targetSchema = cmbTargetDataSchema.Text;
                    // UX#1: Show per-row status while comparing
                    row.Cells["ColIdentical"].Value = "⌛ Comparing...";
                    row.Cells["ColDiff"].Value = "...";
                    row.Cells["ColSource"].Value = "";
                    row.Cells["ColTarget"].Value = "";
                    dgvTableDiffs.Refresh();

                    var options = GetDataCompareOptions();
                    var summary = await _dbCompareService!.GetTableDataDiffSummaryAsync(table, sourceSchema, targetSchema, options);

                    row.Cells["ColDiff"].Value = summary.UpdatedCount;
                    row.Cells["ColSource"].Value = summary.InsertedCount;
                    row.Cells["ColTarget"].Value = summary.DeletedCount;

                    if (summary.HasDifferences)
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 255, 224);
                        row.DefaultCellStyle.ForeColor = Color.DarkRed;
                        row.Cells["ColIdentical"].Value = "Different";
                        diffCount++;
                    }
                    else
                    {
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 230);
                        row.DefaultCellStyle.ForeColor = Color.DarkGreen;
                        row.Cells["ColIdentical"].Value = "Synchronized";
                        syncCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during comparison: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                btnCompareData.Enabled = true;
                btnCompareData.Text = "▶ Start Comparison";
                // UX#2: Summary status bar
                lblDataStatus.Text = $"✔ Done — 🔴 {diffCount} Different  |  🟢 {syncCount} Synchronized  |  ⏱ {sw.Elapsed.TotalSeconds:F1}s";
                lblDataStatus.ForeColor = diffCount > 0 ? Color.DarkRed : Color.DarkGreen;
                pbDataLoading.Visible = false;
                this.Cursor = Cursors.Default;
            }
        }

        private async void DgvTableDiffs_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            // UX#3: Ignore double-click on the checkbox column
            if (dgvTableDiffs.Columns["ColCheck"] != null && e.ColumnIndex == dgvTableDiffs.Columns["ColCheck"].Index) return;

            var row = dgvTableDiffs.Rows[e.RowIndex];
            var table = row.Cells["ColName"].Value?.ToString();
            if (string.IsNullOrEmpty(table)) return;

            var statusStr = row.Cells["ColIdentical"].Value?.ToString() ?? "";
            // Bug#2 fix: use Contains to avoid emoji encoding mismatch; also block Added/Removed (no comparison done)
            if (statusStr == "⌛ Waiting Compare" || statusStr.Contains("No Primary") || statusStr == "Added (New)" || statusStr == "Removed (Old)" || statusStr.Contains("Comparing"))
            {
                MessageBox.Show($"Table '{table}' has not been compared yet.\n\nPlease check the table and click '▶ Start Comparison' first.",
                    "Not Yet Compared", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!EnsureServicesInitialized()) return;

            lblDataStatus.Text = $"⌛ Loading detail for {table}...";
            this.Cursor = Cursors.WaitCursor;
            try
            {
                string sourceSchema = cmbSourceDataSchema.Text;
                string targetSchema = cmbTargetDataSchema.Text;
                var options = GetDataCompareOptions();
                var diffs = await _dbCompareService!.GetDetailedTableDataDiffAsync(table, sourceSchema, targetSchema, options);
                using (var dlg = new DataDiffDialog(table, diffs))
                {
                    dlg.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading details for {table}:\n{ex.ToString()}");
            }
            finally
            {
                lblDataStatus.Text = "";
                this.Cursor = Cursors.Default;
            }
        }

        private async void BtnGenerateData_Click(object? sender, EventArgs e)
        {
            var tablesToSync = GetSelectedTables();
            if (!tablesToSync.Any()) { MessageBox.Show("Please check tables to sync first."); return; }

            if (!EnsureServicesInitialized()) return;

            string sourceSchema = cmbSourceDataSchema.Text;
            string targetSchema = cmbTargetDataSchema.Text;
            
            lblDataStatus.Text = "⌛ Generating synchronization script...";
            this.Cursor = Cursors.WaitCursor;
            
            try
            {
                var options = GetDataCompareOptions();
                var diffScript = await _dbCompareService!.GenerateDataDiffAsync(tablesToSync, sourceSchema, targetSchema, options);
                
                var fileName = $"data_sync_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                var fullPath = _fileSystemService!.SaveSqlScript(fileName, diffScript, false);
                _lastDataExportPath = fullPath;
                
                lblDataStatus.Text = $"✅ Exported to {fileName}";
                btnOpenDataFolder.Visible = true;
                
                if (MessageBox.Show($"Data sync script generated and saved to:\n{fullPath}\n\nWould you like to open it now?", "Export Successful", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating data sync script:\n{ex.Message}");
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        private List<string> GetSelectedTables()
        {
            var selectedTables = new List<string>();
            foreach (DataGridViewRow row in dgvTableDiffs.Rows)
            {
                if (Convert.ToBoolean(row.Cells["ColCheck"].Value))
                {
                    selectedTables.Add(row.Cells["ColName"].Value?.ToString() ?? "");
                }
            }
            return selectedTables;
        }

        private void ApplyTableFilter(string filter)
        {
            // First make all rows visible to reset any previous filter
            foreach (DataGridViewRow row in dgvTableDiffs.Rows)
                row.Visible = true;

            if (filter == "All Tables") return;

            foreach (DataGridViewRow row in dgvTableDiffs.Rows)
            {
                var status = row.Cells["ColIdentical"].Value?.ToString() ?? "";
                bool isChecked = Convert.ToBoolean(row.Cells["ColCheck"].Value);

                bool visible = filter switch
                {
                    "Selected (Checked)"  => isChecked,
                    "Unselected"          => !isChecked && !row.Cells["ColCheck"].ReadOnly,
                    "Different"           => status == "Different",
                    "Synchronized"        => status == "Synchronized",
                    "Added (New)"         => status == "Added (New)",
                    "Removed (Old)"       => status == "Removed (Old)",
                    "⚠️ No PK"           => status.Contains("No Primary Key"),
                    _                     => true
                };
                row.Visible = visible;
            }
        }



        private async void BtnExecuteSchema_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show($"Are you sure you want to execute the SCHEMA synchronization script directly on the TARGET database ({OldDbName})?", 
                "Confirm Execution", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var path = _fileSystemService!.GetSqlScriptPath(NewDbName, true);
                await ExecuteScriptOnOldDb(path, "Schema");
            }
        }

        private async void BtnExecuteData_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show($"Are you sure you want to execute the DATA synchronization script directly on the TARGET database ({OldDbName})?", 
                "Confirm Execution", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var path = _fileSystemService!.GetSqlScriptPath(NewDbName, false);
                await ExecuteScriptOnOldDb(path, "Data");
            }
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

        private async void BtnExportFinal_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            try
            {
                txtFinalExportLog.AppendText($"Starting final export for {OldDbName}...\r\n");
                var backupPath = _fileSystemService!.GetBackupPath(OldDbName);
                await _oldPgService!.BackupDatabaseAsync(backupPath);
                txtFinalExportLog.AppendText($"Backup saved to {backupPath}\r\n");

                var fullPath = _fileSystemService!.GetFullScriptPath(OldDbName);
                await _oldPgService!.DumpFullScriptAsync(fullPath);
                txtFinalExportLog.AppendText($"Full script saved to {fullPath}\r\n");
            }
            catch (Exception ex)
            {
                txtFinalExportLog.AppendText($"Error: {ex.Message}\r\n");
            }
        }

        private async void BtnVerifySync_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            string schemaSS = cmbSourceSchema.Text;
            string schemaST = cmbTargetSchema.Text;
            string schemaDS = cmbSourceDataSchema.Text;
            string schemaDT = cmbTargetDataSchema.Text;
            
            txtExecuteLog.AppendText($"Verifying Sync Status (Schema: {schemaSS}->{schemaST}, Data: {schemaDS}->{schemaDT})...\r\n");
            try
            {
                var schemaScript = await _dbCompareService!.GenerateSchemaDiffAsync(schemaSS, schemaST);
                if (string.IsNullOrWhiteSpace(schemaScript) || (!schemaScript.Contains("ALTER TABLE") && !schemaScript.Contains("CREATE TABLE") && !schemaScript.Contains("DROP ")))
                {
                    txtExecuteLog.AppendText($"-> Schema Verification Passed! No remaining schema differences in '{schemaSS}' vs '{schemaST}'.\r\n");
                }
                else
                {
                    txtExecuteLog.AppendText($"-> Schema Verification FAILED! There are still schema differences. Please check Tab 3.\r\n");
                }
                
                var selectedTables = GetSelectedTables();
                if (selectedTables.Any())
                {
                    var options = GetDataCompareOptions();
                    var dataScript = await _dbCompareService!.GenerateDataDiffAsync(selectedTables, schemaDS, schemaDT, options);
                    if (string.IsNullOrWhiteSpace(dataScript) || (!dataScript.Contains("INSERT INTO") && !dataScript.Contains("UPDATE ") && !dataScript.Contains("DELETE FROM")))
                    {
                        txtExecuteLog.AppendText($"-> Data Verification Passed! Selected tables are synchronized.\r\n");
                    }
                    else
                    {
                        txtExecuteLog.AppendText($"-> Data Verification FAILED! There are still data differences. Please check Tab 4.\r\n");
                    }
                }
            }
            catch (Exception ex)
            {
                txtExecuteLog.AppendText($"Verification Error: {ex.Message}\r\n");
            }
        }

        // --- New Clean Junk Tab Logic ---
        private PostgresService GetActiveJunkPgService()
        {
             if (cmbJunkConnection.SelectedIndex == 0) return _newPgService!;
             if (cmbJunkConnection.SelectedIndex == 1) return _oldPgService!;
             if (cmbJunkConnection.SelectedIndex == 2 && _customJunkPgService != null) return _customJunkPgService;
             return _oldPgService!;
        }

        private async Task UpdateJunkSelectionTreeAsync(bool silent = false)
        {
            if (!EnsureServicesInitialized(silent)) return;
            var service = GetActiveJunkPgService();
            tvJunkSelection.Nodes.Clear();
            
            try {
                var dbs = await service.GetAllDatabasesAsync();
                foreach (var db in dbs.Where(d => !d.StartsWith("pg_") && d != "postgres").OrderBy(d => d))
                {
                    var dbNode = new TreeNode(db) { Tag = "DB" };
                    dbNode.Nodes.Add(new TreeNode("Loading...") { Tag = "DUMMY" }); // Add dummy for expansion
                    tvJunkSelection.Nodes.Add(dbNode);
                }
            } catch (Exception ex) { MessageBox.Show("Error loading DBs: " + ex.Message); }
        }

        private async void TvJunkSelection_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null) return;
            
            // 1. Expand DB to show Schemas
            if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag?.ToString() == "DUMMY" && e.Node.Tag?.ToString() == "DB")
            {
                e.Node.Nodes.Clear();
                var service = GetActiveJunkPgService();
                var dbName = e.Node.Text;

                try {
                    var schemas = await service.GetSchemasAsync(dbName);
                    foreach (var schema in schemas.OrderBy(s => s))
                    {
                        var schemaNode = new TreeNode(schema) { Tag = "SCHEMA", Checked = e.Node.Checked };
                        // Add sub-options for each schema
                        var structNode = new TreeNode("Structure") { Tag = "STRUCT", Checked = schemaNode.Checked };
                        var dataNode = new TreeNode("Data Records") { Tag = "DATA", Checked = schemaNode.Checked };
                        schemaNode.Nodes.Add(structNode);
                        schemaNode.Nodes.Add(dataNode);
                        e.Node.Nodes.Add(schemaNode);
                    }
                } catch { 
                    e.Node.Nodes.Add(new TreeNode("Error loading schemas") { ForeColor = Color.Red });
                }
            }
        }

        private void TvJunkSelection_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (e.Action == TreeViewAction.Unknown || e.Node == null) return;

            // Cascade check to children
            SetTreeViewChecked(e.Node.Nodes, e.Node.Checked);
            
            // Selectively update parent state (simplification: if any child is checked, don't necessarily check parent)
            // But we keep it simple for now as per user screenshot.
        }

        private async void BtnAnalyzeJunk_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            var keywordStr = txtJunkKeywords.Text;
            var keywords = keywordStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList();
            if (!keywords.Any()) { MessageBox.Show("Please enter junk keywords."); return; }

            // Gather granular selections
            var selectedScopes = new List<(string Db, List<SchemaSelection> Selections)>();
            foreach (TreeNode dbNode in tvJunkSelection.Nodes)
            {
                var schemaSelectionsForDb = new List<SchemaSelection>();
                
                // If DB is checked or has checked children
                if (dbNode.Checked || dbNode.Nodes.Cast<TreeNode>().Any(n => n.Checked || n.Nodes.Cast<TreeNode>().Any(sn => sn.Checked)))
                {
                    // If dummy (not expanded), load schemas and assume all if DB checked
                    if (dbNode.Nodes.Count == 1 && dbNode.Nodes[0].Tag?.ToString() == "DUMMY")
                    {
                         if (dbNode.Checked) {
                             var service = GetActiveJunkPgService();
                             var schemas = await service.GetSchemasAsync(dbNode.Text);
                             foreach (var s in schemas)
                                 schemaSelectionsForDb.Add(new SchemaSelection { SchemaName = s, IncludeStructure = true, IncludeData = true });
                         }
                    }
                    else 
                    {
                        foreach (TreeNode schemaNode in dbNode.Nodes)
                        {
                            bool includeStruct = schemaNode.Checked || schemaNode.Nodes.Cast<TreeNode>().Any(sn => sn.Tag?.ToString() == "STRUCT" && sn.Checked);
                            bool includeData = schemaNode.Checked || schemaNode.Nodes.Cast<TreeNode>().Any(sn => sn.Tag?.ToString() == "DATA" && sn.Checked);
                            
                            if (includeStruct || includeData)
                            {
                                schemaSelectionsForDb.Add(new SchemaSelection 
                                { 
                                    SchemaName = schemaNode.Text, 
                                    IncludeStructure = includeStruct, 
                                    IncludeData = includeData 
                                });
                            }
                        }
                    }

                    if (schemaSelectionsForDb.Any())
                        selectedScopes.Add((dbNode.Text, schemaSelectionsForDb));
                }
            }

            if (!selectedScopes.Any()) { MessageBox.Show("Please select at least one database/schema/type to analyze."); return; }

            btnAnalyzeJunk.Enabled = false;
            btnAnalyzeJunk.Text = "⌛ Analyzing...";
            tvJunkResults.Nodes.Clear();
            dgvJunkDataResults.Rows.Clear();
            _lastJunkResults.Clear();

            try {
                _junkService = new JunkAnalysisService(GetActiveJunkPgService());
                
                foreach (var scope in selectedScopes)
                {
                    var dbResults = await _junkService.AnalyzeAsync(new[] { scope.Db }, keywords, scope.Selections);
                    foreach (var res in dbResults)
                    {
                        if (res.Items.Any()) _lastJunkResults.Add(res);
                    }
                }

                foreach (var res in _lastJunkResults)
                {
                    // 1. Populate Structural Tree
                    var structItems = res.Items.Where(i => i.Type != JunkType.DataRecord).ToList();
                    if (structItems.Any())
                    {
                        var dbNode = new TreeNode($"Database: {res.DatabaseName}") { Tag = res.DatabaseName };
                        foreach (var item in structItems)
                        {
                            string schemaLabel = string.IsNullOrEmpty(item.SchemaName) ? "(Database Objects)" : item.SchemaName;
                            var schemaNode = dbNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == schemaLabel);
                            if (schemaNode == null)
                            {
                                schemaNode = new TreeNode(schemaLabel) { Tag = item.SchemaName };
                                dbNode.Nodes.Add(schemaNode);
                            }
                            
                            var typeNode = schemaNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == item.Type.ToString());
                            if (typeNode == null)
                            {
                                typeNode = new TreeNode(item.Type.ToString());
                                schemaNode.Nodes.Add(typeNode);
                            }

                            // Show Name + Reason
                            var itemNode = new TreeNode($"{item.ObjectName} ({item.DetectedContent})")
                            {
                                Tag = item,
                                Checked = true,
                                ToolTipText = item.DetectedContent ?? "Structural junk"
                            };
                            typeNode.Nodes.Add(itemNode);
                        }
                        dbNode.Expand();
                        tvJunkResults.Nodes.Add(dbNode);
                    }

                    // 2. Populate Data Grid
                    var dataItems = res.Items.Where(i => i.Type == JunkType.DataRecord).ToList();
                    
                    // Group by PK to handle 1 record having junk in multiple columns
                    var groupedData = dataItems.GroupBy(i => new { i.DatabaseName, i.SchemaName, i.ObjectName, i.PrimaryKeyValue });
                    
                    foreach (var group in groupedData)
                    {
                        var firstItem = group.First();
                        
                        // Combine column names if multiple columns matched
                        string aggCols = string.Join(", ", group.Select(i => i.ColumnName).Distinct());
                        
                        // Combine actual values (RawData) instead of the "Found in..." reason string
                        string aggVals = string.Join(" | ", group.Select(i => i.RawData?.Replace("\n", " ")?.Replace("\r", "")).Distinct());
                        if (aggVals.Length > 200) aggVals = aggVals.Substring(0, 197) + "...";

                        int rowIndex = dgvJunkDataResults.Rows.Add(true, firstItem.DatabaseName, firstItem.ObjectName, aggCols, firstItem.PrimaryKeyValue, aggVals);
                        dgvJunkDataResults.Rows[rowIndex].Tag = firstItem; // Tagging with the first item is fully sufficient for generating the DELETE script
                    }
                }
                
                if (!_lastJunkResults.Any()) MessageBox.Show("No junk found with these keywords and selections!");
                else
                {
                    // Update: No longer auto-generate/show script in hidden field.
                    // Just inform user or they can click Preview.
                    if (dgvJunkDataResults.Rows.Count > 0 && tvJunkResults.Nodes.Count == 0)
                        tcJunkResults.SelectedIndex = 1;
                }
            }
            catch (Exception ex) { MessageBox.Show("Analysis failed: " + ex.Message); }
            finally {
                btnAnalyzeJunk.Enabled = true;
                btnAnalyzeJunk.Text = "🔍 ANALYZE JUNK";
            }
        }

        private void BtnGenerateJunkScript_Click(object? sender, EventArgs e)
        {
            if (_junkService == null || !_lastJunkResults.Any()) { MessageBox.Show("Please analyze junk first."); return; }
            var selectedItems = GetSelectedJunkItems();
            if (!selectedItems.Any()) { MessageBox.Show("No junk items selected."); return; }

            var script = _junkService.GenerateCleanupScript(selectedItems);
            
            using (var editor = new JunkScriptEditorForm(script))
            {
                if (editor.ShowDialog() == DialogResult.OK)
                {
                    _lastEditedJunkScript = editor.EditedScript;
                    BtnCleanJunk_Click(btnGenerateJunkScript, EventArgs.Empty);
                }
            }
        }

        private string? _lastEditedJunkScript;

        private async void BtnCleanJunk_Click(object? sender, EventArgs e)
        {
            if (_junkService == null) return;
            
            string scriptToRun;
            if (!string.IsNullOrEmpty(_lastEditedJunkScript))
            {
                scriptToRun = _lastEditedJunkScript;
            }
            else
            {
                var selectedItems = GetSelectedJunkItems();
                if (!selectedItems.Any()) { MessageBox.Show("No junk items selected."); return; }
                scriptToRun = _junkService.GenerateCleanupScript(selectedItems);
            }

            var confirm = MessageBox.Show("Are you sure you want to execute the cleanup script? This action cannot be undone.", "Confirm Cleanup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            btnCleanJunk.Enabled = false;
            try
            {
                var service = GetActiveJunkPgService();
                await service.ExecuteSqlWithTransactionAsync(scriptToRun);
                MessageBox.Show("Cleanup executed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _lastEditedJunkScript = null; // Clear after use
                BtnAnalyzeJunk_Click(btnCleanJunk, EventArgs.Empty); // Refresh
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cleanup failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnCleanJunk.Enabled = true;
            }
        }

        private void TvJunkResults_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is JunkItem item)
            {
                using (var dlg = new JunkDetailDialog(item, GetJunkPostgresService()))
                {
                    dlg.ShowDialog();
                }
            }
        }

        private void DgvJunkDataResults_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dgvJunkDataResults.Rows[e.RowIndex].Tag is JunkItem item)
            {
                using (var dlg = new JunkDetailDialog(item, GetJunkPostgresService()))
                {
                    dlg.ShowDialog();
                }
            }
        }

        private void DgvJunkDataResults_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 5 && e.Value != null)
            {
                var item = dgvJunkDataResults.Rows[e.RowIndex].Tag as JunkItem;
                if (item?.MatchedKeywords == null || !item.MatchedKeywords.Any()) return;

                string text = e.Value.ToString() ?? "";
                var matches = new List<(int start, int length)>();
                foreach (var kw in item.MatchedKeywords) {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    int idx = 0;
                    while ((idx = text.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) != -1) {
                        matches.Add((idx, kw.Length));
                        idx += kw.Length;
                    }
                }

                if (!matches.Any()) return;

                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);

                var sortedMatches = matches.OrderBy(m => m.start).ToList();
                int currentPos = 0;
                
                int currentX = e.CellBounds.X + e.CellStyle.Padding.Left + 4;
                int y = e.CellBounds.Y + (e.CellBounds.Height - e.CellStyle.Font.Height) / 2;
                
                Color foreColor = (e.State & DataGridViewElementStates.Selected) != 0 ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
                
                Region oldClip = e.Graphics.Clip;
                e.Graphics.SetClip(e.CellBounds);
                
                try
                {
                    foreach (var match in sortedMatches) {
                        if (match.start < currentPos) continue; 
                        
                        if (match.start > currentPos) {
                            string pre = text.Substring(currentPos, match.start - currentPos);
                            Size size = TextRenderer.MeasureText(e.Graphics, pre, e.CellStyle.Font);
                            TextRenderer.DrawText(e.Graphics, pre, e.CellStyle.Font, new Point(currentX, y), foreColor, TextFormatFlags.NoPadding);
                            currentX += size.Width;
                        }
                        
                        string mid = text.Substring(match.start, match.length);
                        Size midSize = TextRenderer.MeasureText(e.Graphics, mid, e.CellStyle.Font);
                        e.Graphics.FillRectangle(Brushes.Yellow, currentX, e.CellBounds.Y + 2, midSize.Width, e.CellBounds.Height - 4);
                        TextRenderer.DrawText(e.Graphics, mid, e.CellStyle.Font, new Point(currentX, y), Color.Red, TextFormatFlags.NoPadding);
                        currentX += midSize.Width;
                        currentPos = match.start + match.length;
                        
                        if (currentX > e.CellBounds.Right) break;
                    }
                    
                    if (currentPos < text.Length && currentX < e.CellBounds.Right) {
                        string post = text.Substring(currentPos);
                        TextRenderer.DrawText(e.Graphics, post, e.CellStyle.Font, new Point(currentX, y), foreColor, TextFormatFlags.NoPadding);
                    }
                }
                finally
                {
                    e.Graphics.Clip = oldClip;
                }

                e.Paint(e.CellBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
                e.Handled = true;
            }
        }

        private PostgresService? GetJunkPostgresService()
        {
            if (cmbJunkConnection.SelectedIndex == 0) return _oldPgService;
            if (cmbJunkConnection.SelectedIndex == 1) return _newPgService;
            if (cmbJunkConnection.SelectedIndex == 2) return _customJunkPgService;
            return null;
        }

        private void SetAllJunkSelectionCheckState(bool check)
        {
            tvJunkSelection.BeginUpdate();
            foreach (TreeNode node in tvJunkSelection.Nodes)
            {
                node.Checked = check;
                SetNodeCheckStateRecursive(node, check);
            }
            tvJunkSelection.EndUpdate();
        }

        private void SetNodeCheckStateRecursive(TreeNode parentNode, bool check)
        {
            foreach (TreeNode node in parentNode.Nodes)
            {
                node.Checked = check;
                if (node.Nodes.Count > 0) SetNodeCheckStateRecursive(node, check);
            }
        }

        private bool _isChangingCheck = false;
        private void TvJunkResults_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_isChangingCheck || e.Node == null || e.Action == TreeViewAction.Unknown) return;

            _isChangingCheck = true;
            tvJunkResults.BeginUpdate();
            try
            {
                // Recursive downward
                SetNodeCheckStateRecursive(e.Node, e.Node.Checked);
                
                // Update upward
                UpdateParentCheckState(e.Node);
            }
            finally
            {
                tvJunkResults.EndUpdate();
                tvJunkResults.Invalidate(); // Force a clean repaint of the whole control
                _isChangingCheck = false;
            }
        }

        private void UpdateParentCheckState(TreeNode node)
        {
            TreeNode? parent = node.Parent;
            if (parent == null) return;

            bool allChecked = true;
            bool anyChecked = false;

            foreach (TreeNode child in parent.Nodes)
            {
                if (child.Checked) anyChecked = true;
                else allChecked = false;
            }

            // We only set the parent if it's not already in the correct state
            // to avoid unnecessary event triggers
            if (parent.Checked != allChecked)
            {
                parent.Checked = allChecked;
                UpdateParentCheckState(parent);
            }
        }

        private void TvJunkResults_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            // Bảo vệ (Guard): Chặn mọi nỗ lực vẽ ảo của WinForms khi Element không nằm trong vùng khả kiến
            if (e.Node == null || !e.Node.IsVisible || e.Bounds.IsEmpty || e.Bounds.Height <= 0 || e.Bounds.Width <= 0) return;

            Font baseFont = e.Node.NodeFont ?? e.Node.TreeView.Font;
            Color foreColor = e.Node.ForeColor;
            if (foreColor == Color.Empty) foreColor = e.Node.TreeView.ForeColor;
            
            // Bounds calculation: e.Bounds is the text area in OwnerDrawText mode
            // Add a 4px offset to prevent drawing over the OS-rendered checkbox right edge.
            int xOffset = 4;
            // Extend width by 60px because bolding text (Database, Schema nodes) makes it wider than the calculated e.Bounds
            Rectangle clearRect = new Rectangle(e.Bounds.X + xOffset, e.Bounds.Y, e.Bounds.Width - xOffset + 60, e.Bounds.Height);
            
            // Text area can just be the same as clearRect, maybe 1px more for breathing room
            Rectangle textRect = new Rectangle(clearRect.X + 2, clearRect.Y, clearRect.Width - 2, clearRect.Height);

            // Clear background completely first to avoid artifacts, BUT do not start precisely at e.Bounds.X 
            // because high-DPI Windows themes often let the checkbox draw slightly into the label bounds.
            e.Graphics.FillRectangle(SystemBrushes.Window, clearRect);

            // Highlight background if selected
            if ((e.State & TreeNodeStates.Selected) != 0)
            {
                foreColor = SystemColors.HighlightText;
                e.Graphics.FillRectangle(SystemBrushes.Highlight, clearRect);
            }

            // Custom logic for different node types
            if (e.Node.Tag is JunkItem junkItem && junkItem.MatchedKeywords != null && junkItem.MatchedKeywords.Any())
            {
                string text = e.Node.Text;
                int currentX = textRect.X;
                int y = textRect.Y + (textRect.Height - baseFont.Height) / 2;

                // Keyword highlighting logic
                var matches = new List<(int start, int length)>();
                foreach (var kw in junkItem.MatchedKeywords) {
                    int idx = 0;
                    while ((idx = text.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) != -1) {
                        matches.Add((idx, kw.Length));
                        idx += kw.Length;
                    }
                }
                
                var sortedMatches = matches.OrderBy(m => m.start).ToList();
                int currentPos = 0;
                
                foreach (var match in sortedMatches) {
                    if (match.start < currentPos) continue; 
                    if (match.start > currentPos) {
                        string pre = text.Substring(currentPos, match.start - currentPos);
                        Size size = TextRenderer.MeasureText(e.Graphics, pre, baseFont);
                        TextRenderer.DrawText(e.Graphics, pre, baseFont, new Point(currentX, y), foreColor, TextFormatFlags.NoPadding);
                        currentX += size.Width;
                    }
                    
                    string mid = text.Substring(match.start, match.length);
                    Size midSize = TextRenderer.MeasureText(e.Graphics, mid, baseFont);
                    e.Graphics.FillRectangle(Brushes.Yellow, currentX, textRect.Y, midSize.Width, textRect.Height);
                    TextRenderer.DrawText(e.Graphics, mid, baseFont, new Point(currentX, y), Color.Red, TextFormatFlags.NoPadding);
                    currentX += midSize.Width;
                    currentPos = match.start + match.length;
                }
                
                if (currentPos < text.Length) {
                    string post = text.Substring(currentPos);
                    TextRenderer.DrawText(e.Graphics, post, baseFont, new Point(currentX, y), foreColor, TextFormatFlags.NoPadding);
                }
            }
            else
            {
                // Container nodes (Database, Schema, Routine types) - Make them BOLD
                using (Font boldFont = new Font(baseFont, FontStyle.Bold))
                {
                    int y = textRect.Y + (textRect.Height - boldFont.Height) / 2;
                    TextRenderer.DrawText(e.Graphics, e.Node.Text, boldFont, new Point(textRect.X, y), foreColor, TextFormatFlags.NoPadding);
                }
            }
            
            if ((e.State & TreeNodeStates.Focused) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, textRect, foreColor, SystemColors.Highlight);
        }

        private List<JunkItem> GetSelectedJunkItems()
        {
            var list = new List<JunkItem>();
            // 1. From Structure Tree
            foreach (TreeNode dbNode in tvJunkResults.Nodes)
                foreach (TreeNode schemaNode in dbNode.Nodes)
                    foreach (TreeNode typeNode in schemaNode.Nodes)
                        foreach (TreeNode itemNode in typeNode.Nodes)
                            if (itemNode.Checked && itemNode.Tag is JunkItem item)
                                list.Add(item);

            // 2. From Data Grid
            foreach (DataGridViewRow row in dgvJunkDataResults.Rows)
            {
                if (row.Cells["Selected"].Value is bool b && b && row.Tag is JunkItem item)
                {
                    list.Add(item);
                }
            }

            return list;
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

        private void BtnCompareConfig_Click(object? sender, EventArgs e)
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

        private async void BtnReviewSchema_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized() || _fileSystemService == null) return;
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, true);
            if (!File.Exists(path)) { txtAiReviewLog.Text = "Schema script not generated yet."; return; }

            txtAiReviewLog.Text = "Sending script to AI for review...\r\n";
            if (_aiService == null) return;
            var result = await _aiService.ReviewSqlScriptAsync(File.ReadAllText(path), $"Release from {OldDbName} to {NewDbName}");
            txtAiReviewLog.Text = result;
        }

        private async void BtnReviewConfig_Click(object? sender, EventArgs e)
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
                        if (node.Checked && node.Tag is string name)
                        {
                            selectedTables.Add(name);
                        }
                    }
                }
            }
            return selectedTables;
        }
        private DataCompareOptions GetDataCompareOptions()
        {
            return new DataCompareOptions
            {
                IgnoreColumns = txtIgnoreColumns.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim()).ToList(),
                WhereClause = txtDataFilter.Text.Trim(),
                UseUpsert = chkUseUpsert.Checked
            };
        }
    }
}
