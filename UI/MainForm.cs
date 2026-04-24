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
        private ToolTip tooltip = new ToolTip();
        
        // Tab 1: Configuration & Setup
        private TextBox txtProductName = null!, txtPgBinPath = null!, txtAiKey = null!, txtReleaseVersion = null!, txtReleasePath = null!;
        private Label lblOldDbStatus = null!, lblNewDbStatus = null!;
        private Label lblSourceSchemaHeader = null!, lblTargetSchemaHeader = null!;
        private GroupBox gbSourceData = null!, gbTargetData = null!;
        private DatabaseConfig _oldDbConfig = null!, _newDbConfig = null!;
        private Button btnConnect = null!;
        
        // Tab 2: Databases Backup
        private Button btnBackupOld = null!;
        private ComboBox cmbRestoreConnection = null!;

        // Tab 3: Compare DB
        private SplitContainer splitCompare = null!;
        private TreeView treeSchema = null!;
        private Button btnLoadTables = null!;
        private ComboBox cmbSourceDb = null!, cmbTargetDb = null!;
        private ComboBox cmbSourceSchema = null!, cmbTargetSchema = null!;
        // Data Compare
        private ComboBox cmbSourceDataDb = null!, cmbTargetDataDb = null!;
        private ComboBox cmbSourceDataSchema = null!, cmbTargetDataSchema = null!;
        private Button btnCompareData = null!;
        private DataGridView dgvTableDiffs = null!;
        // private TextBox txtDbDiffLog; // Moved to combined declaration

        // Tab 4: Sync & Execute DB
        private Button btnExecuteSchema = null!, btnExecuteData = null!;
        private TextBox txtExecuteLog = null!, txtFinalExportLog = null!, txtBackupLog = null!, txtConfigDiffLog = null!, txtAiReviewLog = null!;
        private FlowLayoutPanel pnlStatusLabels = null!, pnlTreeToolbar = null!;
        private RichTextBox txtSourceDdl = null!, txtTargetDdl = null!, txtSourceLineNumbers = null!, txtTargetLineNumbers = null!;
        private TextBox txtIgnoreColumns = null!, txtDataFilter = null!;
        private CheckBox chkUseUpsert = null!;
        private Label lblDataStatus = null!;
        private List<SchemaDiffResult> _schemaDiffs = new List<SchemaDiffResult>();
        private Label lblSourceDdlHeader = null!, lblTargetDdlHeader = null!;
        private ProgressBar pbDataLoading = null!;
        private Button btnRefreshTables = null!;
        
        // Tab 6: Compare Config
        private TextBox txtOldConfigPath = null!, txtNewConfigPath = null!;
        private Button btnSelectOldConfig = null!, btnSelectNewConfig = null!, btnCompareConfig = null!;    

        // Tab 7: Final Export + Tab 8: AI Review
        private Button btnReviewSchema = null!, btnReviewConfig = null!, btnGenerateSchema = null!;
        private Button btnOpenSchemaFolder = null!, btnOpenDataFolder = null!, btnEditSchema = null!, btnEditData = null!;
        private ComboBox cmbJunkConnection = null!;
        private TreeView tvJunkSelection = null!;
        private TextBox txtJunkKeywords = null!;
        private TreeView tvJunkResults = null!;
        private TabControl tcJunkResults = null!;
        private DataGridView dgvJunkDataResults = null!;
        private SplitContainer _splitJunkData = null!;
        private Panel _pnlJunkDataDetail = null!;
        private RichTextBox _rtbJunkDetail = null!;
        private Label _lblJunkDetailHeader = null!;
        private Button btnAnalyzeJunk = null!, btnCleanJunk = null!, btnGenerateJunkScript = null!;
        private JunkAnalysisService? _junkService;
        private List<JunkAnalysisResult> _lastJunkResults = new();
        private DatabaseConfig? _customJunkConfig, _customRestoreConfig;
        private PostgresService? _customJunkPgService, _customRestorePgService;
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
            
            tabControl = new TabControl {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(0, 34),
                Padding = new Point(16, 8),
                Appearance = TabAppearance.Normal
            };
            tabControl.DrawItem += TabControl_DrawItem;
            tabControl.SelectedIndexChanged += (s, e) => tabControl.Invalidate();
            // Paint over the default gray border that WinForms draws below the tab strip
            tabControl.Paint += (s, e) => {
                using var brush = new SolidBrush(Color.White);
                var strip = tabControl.GetTabRect(0);
                // Fill the 1-2px line between tab headers and page content
                e.Graphics.FillRectangle(brush, 0, strip.Bottom, tabControl.Width, 3);
            };
            this.Controls.Add(tabControl);
        }

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var tab = tabControl.TabPages[e.Index];
            var isSelected = (e.State & DrawItemState.Selected) != 0;

            // Background: White for all, matches the page cards
            using (var bg = new SolidBrush(Color.White))
                e.Graphics.FillRectangle(bg, e.Bounds);

            // Bottom accent line for selected tab
            if (isSelected)
            {
                using (var accent = new SolidBrush(UIConstants.Primary))
                    e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
            }

            // Text
            var textColor = isSelected ? UIConstants.Primary : UIConstants.TextSecondary;
            var font = new Font(UIConstants.MainFontName, 8.5f, isSelected ? FontStyle.Bold : FontStyle.Regular);
            var textRect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, tab.Text, font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            font.Dispose();
        }

        private string OldDbName => _oldDbConfig?.DatabaseName ?? "old_db";
        private string NewDbName => _newDbConfig?.DatabaseName ?? "new_db";

        private void UpdateConnectionLabels()
        {
            UpdateStatusBadge(lblOldDbStatus, _oldDbConfig?.IsValid, _oldDbConfig != null ? $"{_oldDbConfig.Host}:{_oldDbConfig.Port} (DB: {_oldDbConfig.DatabaseName})" : "Not Configured");
            UpdateStatusBadge(lblNewDbStatus, _newDbConfig?.IsValid, _newDbConfig != null ? $"{_newDbConfig.Host}:{_newDbConfig.Port} (DB: {_newDbConfig.DatabaseName})" : "Not Configured");
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


        private void StyleButtonPrimary(Button btn) { btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderSize = 0; btn.BackColor = UIConstants.Primary; btn.ForeColor = UIConstants.White; btn.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold); btn.Cursor = Cursors.Hand; }
        private void StyleButtonSecondary(Button btn) { btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = UIConstants.Border; btn.BackColor = UIConstants.Surface; btn.ForeColor = UIConstants.TextPrimary; btn.Font = new Font(UIConstants.MainFontName, 9f); btn.Cursor = Cursors.Hand; }
        private void StyleButtonDestructive(Button btn) { btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderSize = 0; btn.BackColor = UIConstants.Danger; btn.ForeColor = Color.White; btn.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold); btn.Cursor = Cursors.Hand; }
        private void StyleTextBoxConsole(TextBox txt) { 
            txt.BackColor = UIConstants.ConsoleBg; 
            txt.ForeColor = UIConstants.ConsoleFg; 
            txt.Font = new Font("Consolas", 10f); 
            txt.BorderStyle = BorderStyle.None; 
            // In Windows forms, TextBox padding is limited, so we rely on the container panel's padding
        }
        private void StyleDataGridView(DataGridView dgv) { 
            dgv.BackgroundColor = Color.White; dgv.BorderStyle = BorderStyle.None; dgv.EnableHeadersVisualStyles = false; 
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = UIConstants.Surface; 
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = UIConstants.TextSecondary;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            dgv.ColumnHeadersHeight = 40;
            dgv.GridColor = UIConstants.Border; 
            dgv.DefaultCellStyle.Font = new Font(UIConstants.MainFontName, 9f);
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 242, 252);
            dgv.DefaultCellStyle.SelectionForeColor = UIConstants.TextPrimary;
            dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
        }

        private Panel CreateCardPanel(string title, int width, int height)
        {
            var pnl = new Panel { Width = width, Height = height, BackColor = Color.White, Padding = new Padding(15, 18, 15, 15) };
            pnl.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // Drop shadow (bottom-right lines)
                using (var p = new Pen(Color.FromArgb(220, 220, 220), 2)) {
                    g.DrawLine(p, 3, pnl.Height - 1, pnl.Width - 1, pnl.Height - 1);
                    g.DrawLine(p, pnl.Width - 1, 3, pnl.Width - 1, pnl.Height - 1);
                }
                // Main outer border
                using (var p = new Pen(UIConstants.Border, 1))
                    g.DrawRectangle(p, 0, 0, pnl.Width - 2, pnl.Height - 2);
                // Accent top bar (3px Microsoft Blue)
                using (var b = new SolidBrush(UIConstants.Primary))
                    g.FillRectangle(b, 0, 0, pnl.Width - 2, 3);
            };
            if (!string.IsNullOrEmpty(title)) {
                var lblTitle = new Label { Text = title, Dock = DockStyle.Top, Height = 32, Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.Primary, Padding = new Padding(0, 8, 0, 0) };
                pnl.Controls.Add(lblTitle);
            }
            return pnl;
        }

        private void UpdateStatusBadge(Label lbl, bool? isValid, string info)
        {
            lbl.AutoSize = true;
            lbl.Padding = new Padding(10, 5, 10, 5);
            lbl.Font = new Font(UIConstants.MainFontName, 9f);
            if (isValid == true) {
                lbl.Text = $"✔  Connected: {info}";
                lbl.ForeColor = UIConstants.Success;
                lbl.BackColor = Color.FromArgb(223, 246, 221);
            } else if (isValid == false) {
                lbl.Text = $"✖  Error: {info}";
                lbl.ForeColor = UIConstants.Danger;
                lbl.BackColor = Color.FromArgb(253, 231, 233);
            } else {
                lbl.Text = $"○  Ready to Connect";
                lbl.ForeColor = UIConstants.TextSecondary;
                lbl.BackColor = UIConstants.Surface;
            }
            lbl.TextAlign = ContentAlignment.MiddleLeft;
        }

        private TextBox AddSettingRow(TableLayoutPanel grid, string label, string defaultValue, bool isPath = false, bool isPassword = false)
        {
            var rowIndex = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary };
            // Margin.Top = 8 to vertical center a ~25px textbox in 42px row
            var txt = new TextBox { Text = defaultValue, Dock = DockStyle.Top, Margin = new Padding(10, 8, 10, 0), UseSystemPasswordChar = isPassword, BorderStyle = BorderStyle.FixedSingle, Font = new Font(UIConstants.MainFontName, 9.5f) };
            
            // Focus highlight effect
            txt.GotFocus += (s, e) => { txt.BackColor = Color.FromArgb(232, 242, 252); };
            txt.LostFocus += (s, e) => { txt.BackColor = Color.White; };

            grid.Controls.Add(lbl, 0, rowIndex);
            grid.Controls.Add(txt, 1, rowIndex);

            if (isPath)
            {
                // Use MainFontName so button text "Browse" renders correctly (NOT MDL2 font)
                var btn = new Button { Text = "Browse...", Dock = DockStyle.Top, Height = 28, Margin = new Padding(4, 7, 4, 0), FlatStyle = FlatStyle.Flat };
                btn.FlatAppearance.BorderColor = UIConstants.Primary;
                btn.ForeColor = UIConstants.Primary;
                btn.BackColor = Color.White;
                btn.Font = new Font(UIConstants.MainFontName, 8.5f);
                btn.Click += (s, e) => {
                    using (var fbd = new FolderBrowserDialog { SelectedPath = txt.Text }) {
                        if (fbd.ShowDialog() == DialogResult.OK) txt.Text = fbd.SelectedPath;
                    }
                };
                grid.Controls.Add(btn, 2, rowIndex);
            }
            
            return txt;
        }

        private void SetupUI()
        {
            tabControl.TabPages.Clear(); // Remove any legacy tabs from the designer

            // 1. Config Setup Tab
            var tabConfig = new TabPage("1. Global Setup") { BackColor = Color.White };
            // Use slightly reduced top padding (16 instead of 20) to prevent unnecessary scrollbar
            var panelConfig = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20, 14, 20, 10), AutoScroll = true, WrapContents = false };
            
            var lblIntro = new Label { 
                Text = "Prepare and synchronize database schemas and data between environments. Configure your source and target connections below to begin.", 
                Width = 800, Height = 38, Margin = new Padding(0, 6, 0, 8), Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary 
            };
            panelConfig.Controls.Add(lblIntro);

            // --- SOURCE CONNECTION CARD ---
            // Height=90px: Padding.Top=18 + Title=32 + 2gap + Button=34 + 4bottom = 90
            var cardSource = CreateCardPanel("SOURCE CONNECTION (New/Dev Development)", 750, 90);
            // Button at y=52: just below title (18+32=50) with 2px gap. Height=34 for better clickability.
            var btnConfigNewDb = new Button { Text = "\u2299  Select Source Connection...", Width = 230, Height = 34, Location = new Point(15, 52) };
            StyleButtonSecondary(btnConfigNewDb);
            btnConfigNewDb.Font = new Font(UIConstants.MainFontName, 9f);
            // Badge at y=57: vertically centered within the 34px button zone (52+10=62 center, badge ~24 tall so top=50)
            lblNewDbStatus = new Label { Text = "Not Configured", Location = new Point(258, 57), AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
            UpdateStatusBadge(lblNewDbStatus, null, "");
            btnConfigNewDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Source Database Connection", _newDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _newDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            cardSource.Controls.Add(btnConfigNewDb); cardSource.Controls.Add(lblNewDbStatus);
            panelConfig.Controls.Add(cardSource);

            // --- TARGET CONNECTION CARD --- (same dimensions as source)
            var cardTarget = CreateCardPanel("TARGET CONNECTION (Old/Prod Maintenance)", 750, 90);
            cardTarget.Margin = new Padding(0, 8, 0, 0);
            var btnConfigOldDb = new Button { Text = "\u2299  Select Target Connection...", Width = 230, Height = 34, Location = new Point(15, 52) };
            StyleButtonSecondary(btnConfigOldDb);
            btnConfigOldDb.Font = new Font(UIConstants.MainFontName, 9f);
            lblOldDbStatus = new Label { Text = "Not Configured", Location = new Point(258, 57), AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
            UpdateStatusBadge(lblOldDbStatus, null, "");
            btnConfigOldDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Target Database Connection", _oldDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _oldDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            cardTarget.Controls.Add(btnConfigOldDb); cardTarget.Controls.Add(lblOldDbStatus);
            panelConfig.Controls.Add(cardTarget);

            // --- GENERAL SETTINGS CARD ---
            // Height: 52(offset) + 5×42(rows) + 15(padding bottom) = 277px
            var cardSettings = CreateCardPanel("GENERAL PROJECT SETTINGS", 750, 277);
            cardSettings.Margin = new Padding(0, 8, 0, 0);

            var pnlSettingsGrid = new TableLayoutPanel {
                Location = new Point(0, 52),   // Padding.Top(18) + title(32) + 2px gap
                Width = 720,
                Height = 210,                   // exactly 5 rows × 42px
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 3,
                Padding = new Padding(0, 2, 0, 0)
            };
            pnlSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            pnlSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            txtPgBinPath     = AddSettingRow(pnlSettingsGrid, "PostgreSQL Bin Path:", @"C:\Program Files\PostgreSQL\17\bin", true);
            txtProductName   = AddSettingRow(pnlSettingsGrid, "Product Name:", "ucrm");
            txtReleaseVersion = AddSettingRow(pnlSettingsGrid, "Release Version:", "4.0.3");
            txtReleasePath   = AddSettingRow(pnlSettingsGrid, "Release Output Path:", @"C:\PROJECT_LCM\output", true);
            txtAiKey         = AddSettingRow(pnlSettingsGrid, "AI API Key (optional):", "", false, true);

            cardSettings.Controls.Add(pnlSettingsGrid);
            panelConfig.Controls.Add(cardSettings);

            // Keep grid width in sync with card width
            void SyncSettingsGridWidth() {
                pnlSettingsGrid.Width = Math.Max(200, cardSettings.ClientSize.Width);
            }
            cardSettings.SizeChanged += (s, e) => SyncSettingsGridWidth();
            cardSettings.HandleCreated += (s, e) => SyncSettingsGridWidth();

            tabConfig.Controls.Add(panelConfig);

            // --- STICKY FOOTER: TableLayoutPanel guarantees correct button order ---
            var pnlFooter = new Panel {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = UIConstants.Surface,
                Padding = new Padding(20, 11, 20, 11)
            };
            pnlFooter.Paint += (s, e) => {
                using var pen = new Pen(UIConstants.Border, 1);
                e.Graphics.DrawLine(pen, 0, 0, pnlFooter.Width, 0);
            };

            // TableLayoutPanel: Col 0 = stretch filler | Col 1 = Open Output | Col 2 = Initialize
            // This guarantees Initialize Session is ALWAYS on the far right.
            var tblFooter = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // filler
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // Open Output
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260)); // Initialize Session
            tblFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var btnOpenReleaseFolder = new Button { Text = "Open Output", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            StyleButtonSecondary(btnOpenReleaseFolder); btnOpenReleaseFolder.Font = new Font(UIConstants.MainFontName, 9f);
            btnOpenReleaseFolder.Click += (s, e) => {
                if (Directory.Exists(txtReleasePath.Text)) Process.Start("explorer.exe", txtReleasePath.Text);
                else MessageBox.Show("Release path does not exist yet.");
            };

            btnConnect = new Button { Text = "\u25B6  Initialize Release Session", Dock = DockStyle.Fill };
            StyleButtonPrimary(btnConnect); btnConnect.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold);
            btnConnect.Click += BtnConnect_Click;

            tblFooter.Controls.Add(new Label(), 0, 0);   // empty filler
            tblFooter.Controls.Add(btnOpenReleaseFolder, 1, 0);
            tblFooter.Controls.Add(btnConnect, 2, 0);
            pnlFooter.Controls.Add(tblFooter);
            tabConfig.Controls.Add(pnlFooter);

            // --- RESPONSIVE WIDTH: stretch cards to fill available width ---
            void UpdateTab1Widths() {
                var w = Math.Max(400, panelConfig.ClientSize.Width - panelConfig.Padding.Horizontal - 4);
                lblIntro.Width = w;
                cardSource.Width = w;
                cardTarget.Width = w;
                cardSettings.Width = w;
            }
            panelConfig.SizeChanged += (s, e) => UpdateTab1Widths();
            panelConfig.HandleCreated += (s, e) => UpdateTab1Widths();

            // 2. Restore DB Tab
            var tabBackup = new TabPage("2. Restore Databases") { BackColor = Color.White };
            var panelBackup = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            // --- INTRO ---
            var lblBackupIntro = new Label {
                Text = "Restore a database from a backup file to the target environment. If no name is provided, the current connection name will be used.",
                Dock = DockStyle.Top, Height = 42, Margin = new Padding(0, 0, 0, 10),
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            // --- CARD: Restore Action ---
            var cardRestore = CreateCardPanel("RESTORE FROM BACKUP FILE", 800, 150);
            
            // Row 1: Connection Selection
            var pnlConnRow = new FlowLayoutPanel { Width = 750, Height = 40, FlowDirection = FlowDirection.LeftToRight, Location = new Point(15, 52) };
            var lblConnLabel = new Label { Text = "Target Connection:", Width = 115, Height = 36, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            cmbRestoreConnection = new ComboBox { Name = "cmbRestoreConnection", Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 9f), Margin = new Padding(8, 4, 0, 0) };
            cmbRestoreConnection.Items.AddRange(new string[] { "Source (Dev)", "Target (Prod)", "Custom Connection..." });
            cmbRestoreConnection.SelectedIndex = 0;
            pnlConnRow.Controls.AddRange(new Control[] { lblConnLabel, cmbRestoreConnection });
            cardRestore.Controls.Add(pnlConnRow);

            cmbRestoreConnection.SelectedIndexChanged += async (s, e) => {
                if (cmbRestoreConnection.SelectedIndex == 2) // Custom
                {
                    using (var dlg = new ConnectionDialog("Custom Restore Target", _customRestoreConfig))
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _customRestoreConfig = dlg.Config;
                            _customRestorePgService = new PostgresService(_customRestoreConfig) { PostgresBinPath = txtPgBinPath.Text };
                        }
                    }
                }
            };

            // Row 2: Restore Params
            var pnlRestoreRow = new FlowLayoutPanel { Width = 750, Height = 50, FlowDirection = FlowDirection.LeftToRight, Location = new Point(15, 92) };
            btnBackupOld = new Button { Text = "\u21BB  Restore DB from File...", Width = 230, Height = 36, Margin = new Padding(0, 0, 15, 0) };
            StyleButtonSecondary(btnBackupOld); btnBackupOld.Font = new Font(UIConstants.MainFontName, 9f);
            var lblTargetDb = new Label { Text = "Target DB Name:", Width = 115, Height = 36, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            var txtTargetDbName = new TextBox { Width = 240, Height = 28, Margin = new Padding(8, 4, 5, 0), PlaceholderText = "e.g. my_prod_restore_v1", Font = new Font(UIConstants.MainFontName, 9f) };
            
            btnBackupOld.Click += (object? s, EventArgs e) => {
                var selectedService = cmbRestoreConnection.SelectedIndex == 2 ? _customRestorePgService : (cmbRestoreConnection.SelectedIndex == 1 ? _newPgService : _oldPgService);
                var defaultDbName = cmbRestoreConnection.SelectedIndex == 2 ? (_customRestoreConfig?.DatabaseName ?? "") : (cmbRestoreConnection.SelectedIndex == 1 ? NewDbName : OldDbName);
                
                if (cmbRestoreConnection.SelectedIndex == 2 && selectedService == null) {
                    MessageBox.Show("Please select a valid custom connection first.", "Custom Connection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                RestoreDbAsync(selectedService, defaultDbName, txtTargetDbName.Text.Trim());
            };

            pnlRestoreRow.Controls.AddRange(new Control[] { btnBackupOld, lblTargetDb, txtTargetDbName });
            cardRestore.Controls.Add(pnlRestoreRow);

            // --- LOG HEADER ---
            var lblLogHeader = new Label {
                Text = "\uD83D\uDCDC  RESTORE LOG", Dock = DockStyle.Top, Height = 28,
                Margin = new Padding(0, 10, 0, 4),
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // --- LOG PANEL (responsive) ---
            var pnlBackupLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtBackupLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtBackupLog);
            pnlBackupLog.Controls.Add(txtBackupLog);

            // --- TOP WORK AREA: FlowLayout prevents Intro and Card from overlapping ---
            var pnlBackupTop = new FlowLayoutPanel { 
                Dock = DockStyle.Top, AutoSize = true, 
                FlowDirection = FlowDirection.TopDown, Padding = new Padding(0), 
                WrapContents = false 
            };
            pnlBackupTop.Controls.Add(lblBackupIntro);
            pnlBackupTop.Controls.Add(cardRestore);

            // Build tab — order matters for Dock layout (last added = topmost)
            panelBackup.Controls.Add(pnlBackupLog);      // Fill (bottom priority, added first)
            panelBackup.Controls.Add(lblLogHeader);       // Top
            panelBackup.Controls.Add(pnlBackupTop);       // Top (topmost priority, added last)

            // Responsive width for card
            void UpdateBackupWidths() {
                var w = Math.Max(400, panelBackup.ClientSize.Width - panelBackup.Padding.Horizontal);
                pnlBackupTop.Width = w;
                cardRestore.Width = w;
                pnlRestoreRow.Width = Math.Max(200, w - 30);
            }
            panelBackup.SizeChanged += (s, e) => UpdateBackupWidths();
            panelBackup.HandleCreated += (s, e) => UpdateBackupWidths();

            tabBackup.Controls.Add(panelBackup);


            // 3. Compare Schema Tab
            var tabCompareSchema = new TabPage("3. Compare Schema") { BackColor = Color.White };
            splitCompare = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 220 };

            // --- LEFT PANEL: Selection ---
            var pnlLeft = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            
            // Replaced pnlDbSelection with a standardized Card - Optimized height
            var cardSchemaFilter = CreateCardPanel("SCHEMA FILTER", 256, 225);
            cardSchemaFilter.Dock = DockStyle.Top;
            
            var gridSelection = new TableLayoutPanel { 
                Location = new Point(5, 45), Width = 236, Height = 76, 
                ColumnCount = 4, RowCount = 2, 
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right 
            };
            gridSelection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); 
            gridSelection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            gridSelection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35)); 
            gridSelection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            gridSelection.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            gridSelection.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            void AddFilterItem(string labelText, ComboBox cb, int col, int row) {
                gridSelection.Controls.Add(new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextSecondary }, col, row);
                cb.Dock = DockStyle.Top; cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Margin = new Padding(1, 4, 4, 0);
                gridSelection.Controls.Add(cb, col + 1, row);
            }

            cmbSourceDb = new ComboBox(); AddFilterItem("Src DB:", cmbSourceDb, 0, 0);
            cmbSourceSchema = new ComboBox(); AddFilterItem("Sch:", cmbSourceSchema, 2, 0);
            cmbTargetDb = new ComboBox(); AddFilterItem("Tgt DB:", cmbTargetDb, 0, 1);
            cmbTargetSchema = new ComboBox(); AddFilterItem("Sch:", cmbTargetSchema, 2, 1);

            // Selection Toolbar (All / None) - Relocated to be above tree
            pnlTreeToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 4, 0, 4), BackColor = Color.White };
            
            var btnSelectAll = new Button { Text = UIConstants.IconSelectAll + "  All", Width = 110, Height = 28, Margin = new Padding(0, 0, 4, 0) }; 
            StyleButtonSecondary(btnSelectAll); btnSelectAll.Font = new Font(UIConstants.IconFontName, 8.5f);

            var btnUnselectAll = new Button { Text = UIConstants.IconClear + "  None", Width = 110, Height = 28, Margin = new Padding(4, 0, 0, 0) }; 
            StyleButtonSecondary(btnUnselectAll); btnUnselectAll.Font = new Font(UIConstants.IconFontName, 8.5f);

            pnlTreeToolbar.Controls.Add(btnSelectAll);
            pnlTreeToolbar.Controls.Add(btnUnselectAll);

            // Main Actions: Load Diffs + Refresh (Side-by-side for space efficiency)
            var pnlFilterActions = new TableLayoutPanel { 
                Location = new Point(10, 130), Width = 236, Height = 40, 
                ColumnCount = 2, RowCount = 1,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right 
            };
            pnlFilterActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            pnlFilterActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

            btnLoadTables = new Button { Text = UIConstants.IconPlay + " Load", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
            StyleButtonPrimary(btnLoadTables); btnLoadTables.Font = new Font(UIConstants.IconFontName, 9f, FontStyle.Bold);
            tooltip.SetToolTip(btnLoadTables, "Compare selected databases and schemas");
            
            var btnRefreshDbs = new Button { Text = UIConstants.IconRefresh + " Refresh", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
            StyleButtonSecondary(btnRefreshDbs); btnRefreshDbs.Font = new Font(UIConstants.IconFontName, 8.5f);
            btnRefreshDbs.Click += async (s, e) => await LoadDatabaseListsAsync();

            pnlFilterActions.Controls.Add(btnLoadTables, 0, 0);
            pnlFilterActions.Controls.Add(btnRefreshDbs, 1, 0);

            cardSchemaFilter.Height = 185; // Optimized height
            cardSchemaFilter.Controls.AddRange(new Control[] { gridSelection, pnlFilterActions });

            btnSelectAll.Click += (s, e) => SetTreeViewChecked(treeSchema.Nodes, true);
            btnUnselectAll.Click += (s, e) => SetTreeViewChecked(treeSchema.Nodes, false);
            btnLoadTables.Click += BtnLoadTables_Click;

            var lblHelp = new Label { Text = "\uD83D\uDCA1 Select tree nodes to see DDL diffs\n\uD83D\uDCA1 Check \u2713 for Data Compare sync", Dock = DockStyle.Bottom, Height = 60, ForeColor = UIConstants.TextPrimary, BackColor = Color.FromArgb(242, 247, 252), Font = new Font(UIConstants.MainFontName, 8.5f), Padding = new Padding(12, 10, 8, 10), TextAlign = ContentAlignment.MiddleLeft };
            lblHelp.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(210, 225, 240), 1), 0, 0, lblHelp.Width, 0);
            };
            treeSchema = new TreeView { 
                Dock = DockStyle.Fill, 
                CheckBoxes = true, 
                BorderStyle = BorderStyle.None, 
                ShowNodeToolTips = true, 
                Indent = 38, 
                ItemHeight = 26,
                DrawMode = TreeViewDrawMode.OwnerDrawText
            };
            treeSchema.AfterSelect += TreeSchema_AfterSelect;
            treeSchema.AfterCheck += TreeSchema_AfterCheck;
            treeSchema.DrawNode += TreeSchema_DrawNode;

            pnlLeft.Controls.Add(treeSchema);
            pnlLeft.Controls.Add(pnlTreeToolbar);
            pnlLeft.Controls.Add(lblHelp);
            pnlLeft.Controls.Add(cardSchemaFilter); 
            splitCompare.Panel1.Controls.Add(pnlLeft);

            // --- RIGHT PANEL: Action bar + 2-pane DDL view ---
            var pnlRight = new Panel { Dock = DockStyle.Fill };
            var pnlActionBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlActionBar.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(UIConstants.Border, 1), 0, pnlActionBar.Height - 1, pnlActionBar.Width, pnlActionBar.Height - 1);
            };

            btnGenerateSchema = new Button { Text = UIConstants.IconExport + "  Generate Script", Width = 160, Height = 38, Location = new Point(12, 12) };
            StyleButtonPrimary(btnGenerateSchema); btnGenerateSchema.Font = new Font(UIConstants.IconFontName, 9.5f, FontStyle.Bold);
            tooltip.SetToolTip(btnGenerateSchema, "Generate SQL script for selected differences");

            btnOpenSchemaFolder = new Button { Text = UIConstants.IconFolder, Width = 40, Height = 38, Location = new Point(12, 12), Visible = false };
            StyleButtonSecondary(btnOpenSchemaFolder); btnOpenSchemaFolder.Font = new Font(UIConstants.IconFontName, 12f);
            tooltip.SetToolTip(btnOpenSchemaFolder, "Open export directory");
            
            btnEditSchema = new Button { Text = UIConstants.IconEdit, Width = 40, Height = 38, Location = new Point(12, 12), Visible = false };
            StyleButtonSecondary(btnEditSchema); btnEditSchema.Font = new Font(UIConstants.IconFontName, 12f);
            tooltip.SetToolTip(btnEditSchema, "Edit/Review generated schema script");
            
            pnlStatusLabels = new FlowLayoutPanel { 
                Location = new Point(12, 14),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent
            };
            
            pnlActionBar.Controls.AddRange(new Control[] { btnGenerateSchema, btnOpenSchemaFolder, btnEditSchema, pnlStatusLabels });

            void RepositionActionBar() {
                if (pnlActionBar.Width == 0) return;
                
                int currentX = 12 + 160 + 8; // Start after Gen button
                if (btnOpenSchemaFolder.Visible) {
                    btnOpenSchemaFolder.Location = new Point(currentX, 12);
                    currentX += 40 + 8;
                }
                if (btnEditSchema.Visible) {
                    btnEditSchema.Location = new Point(currentX, 12);
                    currentX += 40 + 8;
                }
                
                currentX += 16; // Margin before badges
                pnlStatusLabels.Location = new Point(currentX, 14);
                
                // Force wrap constraint
                int availableWidth = Math.Max(50, pnlActionBar.Width - currentX - 12);
                pnlStatusLabels.Width = availableWidth;
                
                // Get strictly wrapped height
                int prefHeight = pnlStatusLabels.GetPreferredSize(new Size(availableWidth, 0)).Height;
                int requiredPanelHeight = Math.Max(62, prefHeight + 28);
                
                pnlStatusLabels.Height = prefHeight;
                
                // Set explicit structural bounds to avoid WinForm's 100px default Panel rendering height bug
                if (pnlActionBar.Height != requiredPanelHeight) {
                    pnlActionBar.MinimumSize = new Size(0, requiredPanelHeight);
                    pnlActionBar.Height = requiredPanelHeight;
                }
            }

            pnlActionBar.Resize += (s, e) => RepositionActionBar();
            btnOpenSchemaFolder.VisibleChanged += (s, e) => RepositionActionBar();
            btnEditSchema.VisibleChanged += (s, e) => RepositionActionBar();
            pnlStatusLabels.ControlAdded += (s, e) => RepositionActionBar();
            pnlStatusLabels.ControlRemoved += (s, e) => RepositionActionBar();

            var pnlHeaders = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 36, ColumnCount = 2, BackColor = UIConstants.Surface };
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlHeaders.Paint += (s, e) => {
                var g = e.Graphics;
                g.DrawLine(new Pen(UIConstants.Border, 1), 0, pnlHeaders.Height - 1, pnlHeaders.Width, pnlHeaders.Height - 1);
                g.DrawLine(new Pen(UIConstants.Border, 1), pnlHeaders.Width / 2, 0, pnlHeaders.Width / 2, pnlHeaders.Height);
            };

            Control CreateHeader(string title, string icon, Color color, out Label lblTitle) {
                var p = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 0, 0), WrapContents = false };
                p.Controls.Add(new Label { Text = icon, Font = new Font(UIConstants.IconFontName, 10.5f), ForeColor = color, AutoSize = true, Margin = new Padding(0, 1, 8, 0) });
                lblTitle = new Label { Text = title, Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = color, AutoSize = true };
                p.Controls.Add(lblTitle);
                return p;
            }

            pnlHeaders.Controls.Add(CreateHeader("SOURCE DDL", UIConstants.IconTable, UIConstants.Primary, out lblSourceDdlHeader), 0, 0);
            pnlHeaders.Controls.Add(CreateHeader("TARGET DDL", UIConstants.IconDatabase, UIConstants.TextSecondary, out lblTargetDdlHeader), 1, 0);

            var pnlDdl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45f)); // Source Line Nums
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Source DDL
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45f)); // Target Line Nums
            pnlDdl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Target DDL
            
            txtSourceLineNumbers = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(248, 248, 248), Font = new Font("Consolas", 10.5f), BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.None, ForeColor = Color.DarkGray };
            txtSourceDdl = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 10.5f), BorderStyle = BorderStyle.None, WordWrap = false };
            txtTargetLineNumbers = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(248, 248, 248), Font = new Font("Consolas", 10.5f), BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.None, ForeColor = Color.DarkGray };
            txtTargetDdl = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 10.5f), BorderStyle = BorderStyle.None, WordWrap = false };
            
            pnlDdl.Controls.Add(txtSourceLineNumbers, 0, 0);
            pnlDdl.Controls.Add(txtSourceDdl, 1, 0);
            pnlDdl.Controls.Add(txtTargetLineNumbers, 2, 0);
            pnlDdl.Controls.Add(txtTargetDdl, 3, 0);

            // Ensure sub-panels are set to Fill their respective TableLayout cells
            pnlActionBar.Dock = DockStyle.Fill;
            pnlHeaders.Dock = DockStyle.Fill;
            pnlDdl.Dock = DockStyle.Fill;

            // New Structural Approach: Use TableLayoutPanel for guaranteed vertical stacking
            var tblRightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            tblRightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Row 0: Action Bar (dynamic wrap height)
            tblRightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Row 1: Headers (DDL Labels)
            tblRightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Row 2: DDL View (Code Panes)
            pnlRight.Controls.Add(tblRightLayout);

            tblRightLayout.Controls.Add(pnlActionBar, 0, 0);
            tblRightLayout.Controls.Add(pnlHeaders, 0, 1);
            tblRightLayout.Controls.Add(pnlDdl, 0, 2);

            // Wire up scroll synchronization
            txtSourceDdl.VScroll += (s, e) => SyncGutterScroll(txtSourceDdl, txtSourceLineNumbers);
            txtTargetDdl.VScroll += (s, e) => SyncGutterScroll(txtTargetDdl, txtTargetLineNumbers);
            
            // MouseWheel sync for gutters
            txtSourceLineNumbers.MouseWheel += (s, e) => { /* already handled by DDL pane hopefully */ };

            splitCompare.Panel2.Controls.Add(pnlRight);
            tabCompareSchema.Controls.Add(splitCompare);
            
            cmbSourceDb.SelectedIndexChanged += async (s, e) => { if (!_suppressComboEvents) await LoadSchemaListsAsync(cmbSourceDb.Text, cmbSourceSchema, _newDbConfig); };
            cmbTargetDb.SelectedIndexChanged += async (s, e) => { if (!_suppressComboEvents) await LoadSchemaListsAsync(cmbTargetDb.Text, cmbTargetSchema, _oldDbConfig); };
            btnGenerateSchema.Click += BtnGenerateSchema_Click;
            btnOpenSchemaFolder.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastSchemaExportPath) && File.Exists(_lastSchemaExportPath)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSchemaExportPath}\"") { UseShellExecute = true }); };
            btnEditSchema.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastSchemaExportPath)) OpenSqlEditor(_lastSchemaExportPath, "Review Schema Migration Script"); };
            
            // treeSchema handles its own events


            // 4. Compare Data Tab
            var tabCompareData = new TabPage("4. Compare Data") { BackColor = Color.White };
            var pnlDataMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 5) };
            
            // Top: Combined Configuration & Actions Card (Ultra High-Density)
            var cardDataSelection = CreateCardPanel("", 800, 165);
            cardDataSelection.Dock = DockStyle.Top;
            var pnlDataGrid = new TableLayoutPanel { Location = new Point(0, 15), Width = 780, Height = 60, ColumnCount = 3, Padding = new Padding(15, 0, 15, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            pnlDataGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            pnlDataGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            pnlDataGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            void AddDataSelectionPanel(TableLayoutPanel parent, string title, ComboBox db, ComboBox schema, Color color, int col) {
                var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                db.Dock = DockStyle.Top; db.DropDownStyle = ComboBoxStyle.DropDownList;
                schema.Dock = DockStyle.Top; schema.DropDownStyle = ComboBoxStyle.DropDownList;
                p.Controls.Add(new Label { Text = title + " DB:", Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold), ForeColor = color, TextAlign = ContentAlignment.BottomLeft, AutoSize = true, Dock = DockStyle.Bottom }, 0, 0);
                p.Controls.Add(db, 0, 1);
                p.Controls.Add(new Label { Text = title + " SCHEMA:", Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold), ForeColor = color, TextAlign = ContentAlignment.BottomLeft, AutoSize = true, Dock = DockStyle.Bottom }, 1, 0);
                p.Controls.Add(schema, 1, 1);
                parent.Controls.Add(p, col, 0);
                db.GotFocus += (s, e) => { db.BackColor = Color.FromArgb(232, 242, 252); };
                db.LostFocus += (s, e) => { db.BackColor = Color.White; };
                schema.GotFocus += (s, e) => { schema.BackColor = Color.FromArgb(232, 242, 252); };
                schema.LostFocus += (s, e) => { schema.BackColor = Color.White; };
            }

            cmbSourceDataDb = new ComboBox(); cmbSourceDataSchema = new ComboBox();
            AddDataSelectionPanel(pnlDataGrid, "SOURCE", cmbSourceDataDb, cmbSourceDataSchema, UIConstants.Primary, 0);

            var pnlArrow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            pnlArrow.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            pnlArrow.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var lblArrow = new Label { 
                Text = "\u279C", 
                AutoSize = false, 
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold), 
                ForeColor = UIConstants.TextSecondary,
                Dock = DockStyle.Top,
                Height = 24
            };
            pnlArrow.Controls.Add(lblArrow, 0, 1);
            pnlDataGrid.Controls.Add(pnlArrow, 1, 0);

            cmbTargetDataDb = new ComboBox(); cmbTargetDataSchema = new ComboBox();
            AddDataSelectionPanel(pnlDataGrid, "TARGET", cmbTargetDataDb, cmbTargetDataSchema, UIConstants.Primary, 2);

            cardDataSelection.Controls.Add(pnlDataGrid);

            // Responsive Width for Selection Card
            void UpdateDataSetupWidth() { 
                cardDataSelection.Width = pnlDataMain.ClientSize.Width - 40; 
                pnlDataGrid.Width = cardDataSelection.ClientSize.Width - 30; 
            }
            pnlDataMain.SizeChanged += (s, e) => UpdateDataSetupWidth();

            // Focus highlighting for Data ComboBoxes
            foreach (var cb in new[] { cmbSourceDataDb, cmbSourceDataSchema, cmbTargetDataDb, cmbTargetDataSchema }) {
                cb.GotFocus += (s, e) => { cb.BackColor = Color.FromArgb(232, 242, 252); };
                cb.LostFocus += (s, e) => { cb.BackColor = Color.White; };
            }

            // Separate Options Row beneath DB setup
            var pnlDataOptions = new FlowLayoutPanel { Location = new Point(14, 75), Size = new Size(760, 30), FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
            
            pnlDataOptions.Controls.Add(new Label { Text = "Advanced Options:", AutoSize = true, Margin = new Padding(0, 6, 15, 0), Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold), ForeColor = UIConstants.Primary });

            pnlDataOptions.Controls.Add(new Label { Text = "Ignore (CSV):", AutoSize = true, Margin = new Padding(0, 6, 3, 0), Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary });
            txtIgnoreColumns = new TextBox { Width = 110, Margin = new Padding(0, 2, 10, 0), PlaceholderText = "e.g. updated_at" };
            pnlDataOptions.Controls.Add(txtIgnoreColumns);
            
            pnlDataOptions.Controls.Add(new Label { Text = "Filter (WHERE):", AutoSize = true, Margin = new Padding(0, 6, 3, 0), Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary });
            txtDataFilter = new TextBox { Width = 110, Margin = new Padding(0, 2, 10, 0), PlaceholderText = "e.g. id > 1000" };
            pnlDataOptions.Controls.Add(txtDataFilter);
            
            chkUseUpsert = new CheckBox { Text = "Use UPSERT", AutoSize = true, Margin = new Padding(0, 5, 0, 0), Checked = true, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary };
            pnlDataOptions.Controls.Add(chkUseUpsert);
            cardDataSelection.Controls.Add(pnlDataOptions);
            pnlDataOptions.BringToFront(); // Show above card decorations
            var pnlDataActions = new FlowLayoutPanel { Location = new Point(14, 105), Size = new Size(760, 56), Padding = new Padding(0, 9, 0, 9), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
            btnCompareData = new Button { Name = "btnCompareData", Text = UIConstants.IconPlay + "  Compare", Width = 110, Height = 38, Margin = new Padding(0) };
            StyleButtonPrimary(btnCompareData); btnCompareData.Font = new Font(UIConstants.IconFontName, 9.5f, FontStyle.Bold);
            tooltip.SetToolTip(btnCompareData, "Start data comparison between selected schemas");
            btnCompareData.Click += BtnCompareData_Click;
            pnlDataActions.Controls.Add(btnCompareData);

            btnRefreshTables = new Button { Text = UIConstants.IconRefresh + "  Load", Width = 100, Height = 38, Margin = new Padding(8, 0, 0, 0) };
            StyleButtonSecondary(btnRefreshTables); btnRefreshTables.Font = new Font(UIConstants.IconFontName, 9.5f);
            tooltip.SetToolTip(btnRefreshTables, "Fetch latest table lists");
            btnRefreshTables.Click += (s, e) => BtnLoadDataTables_Click(null!, null!);
            pnlDataActions.Controls.Add(btnRefreshTables);

            var btnGenerateData = new Button { Text = UIConstants.IconExport + "  Generate Script", Width = 145, Height = 38, Margin = new Padding(8, 0, 0, 0) };
            StyleButtonSecondary(btnGenerateData); btnGenerateData.Font = new Font(UIConstants.IconFontName, 9.5f);
            tooltip.SetToolTip(btnGenerateData, "Generate data synchronization script");
            btnGenerateData.Click += BtnGenerateData_Click;
            pnlDataActions.Controls.Add(btnGenerateData);

            btnOpenDataFolder = new Button { Text = UIConstants.IconFolder, Width = 45, Height = 38, Margin = new Padding(8, 0, 0, 0), Visible = false };
            StyleButtonSecondary(btnOpenDataFolder); btnOpenDataFolder.Font = new Font(UIConstants.IconFontName, 12f);
            tooltip.SetToolTip(btnOpenDataFolder, "Open data export folder");
            btnOpenDataFolder.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastDataExportPath) && File.Exists(_lastDataExportPath)) Process.Start("explorer.exe", $"/select,\"{_lastDataExportPath}\""); };
            pnlDataActions.Controls.Add(btnOpenDataFolder);

            btnEditData = new Button { Text = UIConstants.IconEdit, Width = 45, Height = 38, Margin = new Padding(8, 0, 0, 0), Visible = false };
            StyleButtonSecondary(btnEditData); btnEditData.Font = new Font(UIConstants.IconFontName, 12f);
            tooltip.SetToolTip(btnEditData, "Edit/Review generated data sync script");
            btnEditData.Click += (s, e) => { if (!string.IsNullOrEmpty(_lastDataExportPath)) OpenSqlEditor(_lastDataExportPath, "Review Data Sync Script"); };
            pnlDataActions.Controls.Add(btnEditData);

            var lblSeparator = new Label { Width = 2, Height = 30, BackColor = UIConstants.Border, Margin = new Padding(15, 3, 15, 0) };

            var lblFilter = new Label { Text = "Filter View:", AutoSize = false, Width = 80, Height = 24, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), Margin = new Padding(10, 7, 0, 0) };
            var cmbFilter = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(5, 7, 0, 0) };
            cmbFilter.Items.AddRange(new object[] { "All Tables", "Selected (checked)", "Unselected", "Different", "Synchronized", "Added (New)", "Removed (Old)", "\u26A0 No PK" });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += (s, e) => ApplyTableFilter(cmbFilter.SelectedItem?.ToString() ?? "All Tables");

            var btnSelectAllData = new Button { Text = UIConstants.IconSelectAll + "  All", Width = 85, Height = 28, Margin = new Padding(15, 5, 4, 0) }; 
            StyleButtonSecondary(btnSelectAllData); btnSelectAllData.Font = new Font(UIConstants.IconFontName, 8.5f);

            var btnUnselectAllData = new Button { Text = UIConstants.IconClear + "  None", Width = 85, Height = 28, Margin = new Padding(4, 5, 0, 0) }; 
            StyleButtonSecondary(btnUnselectAllData); btnUnselectAllData.Font = new Font(UIConstants.IconFontName, 8.5f);

            btnSelectAllData.Click += (s, e) => {
                foreach (DataGridViewRow row in dgvTableDiffs.Rows) row.Cells["ColCheck"].Value = true;
                dgvTableDiffs.EndEdit();
            };
            btnUnselectAllData.Click += (s, e) => {
                foreach (DataGridViewRow row in dgvTableDiffs.Rows) row.Cells["ColCheck"].Value = false;
                dgvTableDiffs.EndEdit();
            };

            lblDataStatus = new Label { Text = "Ready", AutoSize = false, Width = 300, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(15, 7, 0, 0), Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Italic), ForeColor = UIConstants.Primary };
            pbDataLoading = new ProgressBar { Width = 120, Height = 18, Style = ProgressBarStyle.Marquee, Visible = false, Margin = new Padding(10, 10, 0, 0) };

            pnlDataActions.Controls.AddRange(new Control[] { lblFilter, cmbFilter, btnSelectAllData, btnUnselectAllData, lblDataStatus, pbDataLoading });
            cardDataSelection.Controls.Add(pnlDataActions);
            pnlDataActions.BringToFront();

            // Main: DataGridView for Table Summary
            dgvTableDiffs = new DataGridView {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                ColumnHeadersVisible = true,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Name = "dgvTableDiffs",
                Margin = new Padding(0, 10, 0, 0),
                RowTemplate = { Height = 28 }
            };
            StyleDataGridView(dgvTableDiffs);

            var chkCol = new DataGridViewCheckBoxColumn {
                Name = "ColCheck",
                HeaderText = "✓",
                Width = 40,
                MinimumWidth = 40,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FillWeight = 1,
                TrueValue = true,
                FalseValue = false,
                FlatStyle = FlatStyle.Flat
            };
            dgvTableDiffs.Columns.Add(chkCol);
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColName",    HeaderText = "TABLE NAME",        FillWeight = 40 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDiff",    HeaderText = "CHANGES",        FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColSource",  HeaderText = "SOURCE ROWS",  FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTarget",  HeaderText = "TARGET ROWS",FillWeight = 15 });
            dgvTableDiffs.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColIdentical",HeaderText = "STATUS",           FillWeight = 15 });

            // Single-click checkbox toggle
            dgvTableDiffs.CellContentClick += (s, ev) => {
                if (dgvTableDiffs.Columns["ColCheck"] != null && ev.ColumnIndex == dgvTableDiffs.Columns["ColCheck"].Index && ev.RowIndex >= 0)
                    dgvTableDiffs.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            dgvTableDiffs.CellDoubleClick += DgvTableDiffs_CellDoubleClick;

            pnlDataMain.Controls.Add(cardDataSelection);
            pnlDataMain.Controls.Add(dgvTableDiffs);
            
            // Enforce flawless top-to-bottom layout hierarchy
            cardDataSelection.BringToFront(); // Laid out 1st -> Top Edge
            dgvTableDiffs.BringToFront();     // Laid out 2nd (Fill) -> Grabs remainder

            var lblHint = new Label { Text = "\uD83D\uDCA1 Double-click a compared row to view detailed record differences.", Dock = DockStyle.Bottom, Height = 25, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font, FontStyle.Italic) };
            pnlDataMain.Controls.Add(lblHint);
            lblHint.SendToBack();

            tabCompareData.Controls.Add(pnlDataMain);

            // 5. Execute Sync Tab
            // 5. Execute Sync Tab
            var tabSyncDb = new TabPage("5. Execute Sync") { BackColor = Color.FromArgb(249, 249, 250) };
            var panelSync = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 12) };

            var mainLayout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlHeader.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(UIConstants.Border, 1), 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            // 1. Unified Header - Left Section (Description)
            var pnlIntro = new Panel { Dock = DockStyle.Left, Width = 500, Padding = new Padding(16, 16, 0, 0) };
            var lblSyncTitle = new Label {
                Text = "DATABASE SYNCHRONIZATION",
                Font = new Font(UIConstants.MainFontName, 11f, FontStyle.Bold),
                ForeColor = UIConstants.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 12)
            };
            var lblSyncDesc = new Label {
                Text = "Execute scripts against target database",
                Font = new Font(UIConstants.MainFontName, 8.5f),
                ForeColor = UIConstants.TextSecondary,
                AutoSize = true,
                Location = new Point(16, 34)
            };
            pnlIntro.Controls.AddRange(new Control[] { lblSyncTitle, lblSyncDesc });

            // 2. Unified Header - Right Section (Toolbar)
            var pnlToolbar = new FlowLayoutPanel { 
                Dock = DockStyle.Right, 
                AutoSize = true,
                Padding = new Padding(0, 18, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            
            var lblStatus = new Label { 
                Text = UIConstants.IconCheck + " System Ready", 
                Font = new Font(UIConstants.MainFontName, 7.5f),
                ForeColor = Color.ForestGreen,
                AutoSize = true,
                Margin = new Padding(0, 10, 16, 0)
            };
            
            btnExecuteSchema = new Button { 
                Text = UIConstants.IconSync + "  Schema Sync", 
                AutoSize = true, Height = 38,
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 244, 242), // Light Red-ish tint
                ForeColor = UIConstants.Danger
            };
            btnExecuteSchema.FlatAppearance.BorderColor = UIConstants.Danger;
            btnExecuteSchema.FlatAppearance.BorderSize = 1;

            btnExecuteData = new Button { 
                Text = UIConstants.IconSync + "  Data Sync", 
                AutoSize = true, Height = 38,
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 248, 240), // Light Orange-ish tint
                ForeColor = UIConstants.Warning
            };
            btnExecuteData.FlatAppearance.BorderColor = UIConstants.Warning;
            btnExecuteData.FlatAppearance.BorderSize = 1;

            var btnVerifySync = new Button { 
                Text = UIConstants.IconCheck + "  Verify Sync Status", 
                AutoSize = true, Height = 38,
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 247, 255), // Light Blue tint
                ForeColor = UIConstants.Primary
            };
            btnVerifySync.FlatAppearance.BorderColor = UIConstants.Primary;
            btnVerifySync.FlatAppearance.BorderSize = 1;

            pnlToolbar.Controls.AddRange(new Control[] { lblStatus, btnExecuteSchema, btnExecuteData, btnVerifySync });
            
            pnlHeader.Controls.Add(pnlIntro);
            pnlHeader.Controls.Add(pnlToolbar);

            // 3. Log Card
            var cardLog = new Panel { 
                Dock = DockStyle.Fill, 
                BackColor = Color.White,
                Padding = new Padding(1) // Border
            };
            cardLog.Paint += (s, e) => {
                using (var pen = new Pen(UIConstants.Border, 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, cardLog.Width - 1, cardLog.Height - 1);
            };

            var pnlLogHeader = new Panel { 
                Dock = DockStyle.Top, 
                Height = 38, 
                BackColor = Color.FromArgb(250, 250, 252) 
            };
            pnlLogHeader.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(UIConstants.Border, 1), 0, pnlLogHeader.Height-1, pnlLogHeader.Width, pnlLogHeader.Height-1);
            };

            var lblLogTitle = new Label {
                Text = UIConstants.IconRefresh + "  EXECUTION TERMINAL",
                Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold),
                ForeColor = UIConstants.Primary, // Tint header icon/text with primary color
                AutoSize = true,
                Location = new Point(16, 11)
            };
            
            var btnClearLog = new Button {
                Text = UIConstants.IconTrash + "  Clear Log",
                Font = new Font(UIConstants.IconFontName, 8.5f),
                Width = 90, Height = 24,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                BackColor = Color.White,
                ForeColor = UIConstants.TextSecondary
            };
            btnClearLog.FlatAppearance.BorderColor = UIConstants.Border;
            
            pnlLogHeader.Controls.Add(lblLogTitle);
            pnlLogHeader.Controls.Add(btnClearLog);
            pnlLogHeader.SizeChanged += (s, e) => {
                btnClearLog.Location = new Point(pnlLogHeader.Width - btnClearLog.Width - 14, 7);
            };

            txtExecuteLog = new TextBox { 
                Multiline = true, 
                Dock = DockStyle.Fill, 
                ScrollBars = ScrollBars.Vertical, 
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
            };
            StyleTextBoxConsole(txtExecuteLog);
            txtExecuteLog.Font = new Font("Consolas", 10f);

            var pnlLogContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = UIConstants.ConsoleBg };
            pnlLogContainer.Controls.Add(txtExecuteLog);

            cardLog.Controls.Add(pnlLogContainer);
            cardLog.Controls.Add(pnlLogHeader);

            mainLayout.Controls.Add(pnlHeader, 0, 0);
            mainLayout.Controls.Add(cardLog, 0, 1);

            panelSync.Controls.Add(mainLayout);
            tabSyncDb.Controls.Add(panelSync);

            // Wiring
            btnExecuteSchema.Click += BtnExecuteSchema_Click;
            btnExecuteData.Click += BtnExecuteData_Click;
            btnVerifySync.Click += BtnVerifySync_Click;
            btnClearLog.Click += (s, e) => txtExecuteLog.Clear();

            // 6. Clean Junk Tab
            var tabCleanJunk = new TabPage("6. Clean Junk") { BackColor = Color.White };
            var pnlJunkRoot = new TableLayoutPanel { 
                Dock = DockStyle.Fill, 
                ColumnCount = 1, RowCount = 2, 
                Padding = new Padding(24, 12, 24, 12) 
            };
            pnlJunkRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            pnlJunkRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // A. Unified Header
            var pnlJunkHeader = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            pnlJunkHeader.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(UIConstants.Border, 1), 0, pnlJunkHeader.Height - 1, pnlJunkHeader.Width, pnlJunkHeader.Height - 1);
            };

            var pnlJunkIntro = new Panel { Dock = DockStyle.Left, Width = 500, Padding = new Padding(0, 16, 0, 0) };
            var lblJunkTitleHeader = new Label {
                Text = "JUNK OBJECT CLEANUP",
                Font = new Font(UIConstants.MainFontName, 11f, FontStyle.Bold),
                ForeColor = UIConstants.TextPrimary,
                AutoSize = true,
                Location = new Point(0, 12)
            };
            var lblJunkDescHeader = new Label {
                Text = "Analyze and remove temporary or unused database objects",
                Font = new Font(UIConstants.MainFontName, 8.5f),
                ForeColor = UIConstants.TextSecondary,
                AutoSize = true,
                Location = new Point(0, 34)
            };
            pnlJunkIntro.Controls.AddRange(new Control[] { lblJunkTitleHeader, lblJunkDescHeader });

            var pnlJunkToolbar = new FlowLayoutPanel { 
                Dock = DockStyle.Right, 
                AutoSize = true,
                Padding = new Padding(0, 18, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var lblJunkStatus = new Label { 
                Text = "✔️ System Ready", 
                Font = new Font(UIConstants.MainFontName, 8.5f),
                ForeColor = Color.ForestGreen,
                AutoSize = true,
                Margin = new Padding(0, 10, 16, 0)
            };

            btnAnalyzeJunk = new Button { 
                Text = "🔍 Analyze Junk", 
                AutoSize = true, Height = 38,
                Margin = new Padding(0, 0, 0, 0),
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 247, 255),
                ForeColor = UIConstants.Primary
            };
            btnAnalyzeJunk.FlatAppearance.BorderColor = UIConstants.Primary;

            btnGenerateJunkScript = new Button { 
                Text = "👁️ Preview", 
                AutoSize = true, Height = 38,
                Margin = new Padding(8, 0, 0, 0),
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(250, 250, 252),
                ForeColor = UIConstants.TextSecondary
            };
            btnGenerateJunkScript.FlatAppearance.BorderColor = UIConstants.Border;

            btnCleanJunk = new Button { 
                Text = "🗑️ Purge Junk", 
                AutoSize = true, Height = 38,
                Margin = new Padding(8, 0, 0, 0),
                Padding = new Padding(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 244, 242),
                ForeColor = UIConstants.Danger
            };
            btnCleanJunk.FlatAppearance.BorderColor = UIConstants.Danger;

            pnlJunkToolbar.Controls.AddRange(new Control[] { lblJunkStatus, btnAnalyzeJunk, btnGenerateJunkScript, btnCleanJunk });
            pnlJunkHeader.Controls.AddRange(new Control[] { pnlJunkIntro, pnlJunkToolbar });

            // B. Content Layout (Sidebar + Main)
            var pnlJunkContent = new TableLayoutPanel { 
                Dock = DockStyle.Fill, 
                ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 16, 0, 0)
            };
            pnlJunkContent.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            pnlJunkContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Left Sidebar Setup
            var pnlLeftSidebar = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };

            // Sidebar Card 1: Configuration
            var cardSidebar = CreateCardPanel("CONFIGURATION", 290, 200);
            cardSidebar.Dock = DockStyle.Top;

            var pnlSidebarBody = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1, RowCount = 4
            };
            
            var lblCmbConn = new Label { Text = "Database Connection:", Font = new Font(UIConstants.MainFontName, 8.5f), Dock = DockStyle.Top, Height = 20 };
            cmbJunkConnection = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 32 };
            cmbJunkConnection.Items.AddRange(new string[] { "Source (Dev)", "Target (Prod)", "Custom Connection..." });

            var lblTxtKeys = new Label { Text = "Keywords (Pattern Search):", Font = new Font(UIConstants.MainFontName, 8.5f), Dock = DockStyle.Top, Height = 20, Margin = new Padding(0, 12, 0, 0) };
            txtJunkKeywords = new TextBox { Dock = DockStyle.Top, Text = "test, dev, tmp, 123", Height = 32 };

            pnlSidebarBody.Controls.AddRange(new Control[] { lblCmbConn, cmbJunkConnection, lblTxtKeys, txtJunkKeywords });
            cardSidebar.Controls.Add(pnlSidebarBody);
            pnlSidebarBody.BringToFront();

            // Sidebar Card 2: Selection Scope
            var cardScope = CreateCardPanel("SELECTION SCOPE", 290, 200);
            cardScope.Dock = DockStyle.Fill;
            cardScope.Padding = new Padding(0, 16, 0, 0); // Spacing between config and scope

            var pnlScopeLinks = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 6, 0, 0) };
            var btnSelectAllScope = new Button { Text = UIConstants.IconCheck + "  All", Width = 65, Height = 24, Margin = new Padding(0, 0, 6, 0) }; 
            StyleButtonSecondary(btnSelectAllScope); btnSelectAllScope.Font = new Font(UIConstants.IconFontName, 8f);
            var btnUnselectAllScope = new Button { Text = UIConstants.IconClear + "  None", Width = 65, Height = 24, Margin = new Padding(0) }; 
            StyleButtonSecondary(btnUnselectAllScope); btnUnselectAllScope.Font = new Font(UIConstants.IconFontName, 8f);
            pnlScopeLinks.Controls.AddRange(new Control[] { btnSelectAllScope, btnUnselectAllScope });

            tvJunkSelection = new TreeView {
                Dock = DockStyle.Fill, CheckBoxes = true, BorderStyle = BorderStyle.None,
                Font = new Font(UIConstants.MainFontName, 9.5f),
                Margin = new Padding(0, 4, 0, 0)
            };
            
            cardScope.Controls.Add(tvJunkSelection);
            cardScope.Controls.Add(pnlScopeLinks);
            tvJunkSelection.BringToFront();
            
            // Add top down (Bottom first for container Dock)
            pnlLeftSidebar.Controls.Add(cardScope);
            pnlLeftSidebar.Controls.Add(cardSidebar);

            // Right View: Detected Objects
            var cardResults = CreateCardPanel("DETECTED OBJECTS", 300, 200);
            cardResults.Dock = DockStyle.Fill;

            var pnlResultToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 6, 0, 0) };
            var btnSelectAllResult = new Button { Text = UIConstants.IconCheck + "  Select All", Width = 110, Height = 26, Margin = new Padding(0, 0, 6, 0) };
            StyleButtonSecondary(btnSelectAllResult); btnSelectAllResult.Font = new Font(UIConstants.IconFontName, 8.5f);
            var btnUnselectAllResult = new Button { Text = UIConstants.IconClear + "  Unselect All", Width = 110, Height = 26, Margin = new Padding(0) };
            StyleButtonSecondary(btnUnselectAllResult); btnUnselectAllResult.Font = new Font(UIConstants.IconFontName, 8.5f);
            pnlResultToolbar.Controls.AddRange(new Control[] { btnSelectAllResult, btnUnselectAllResult });

            tcJunkResults = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(12) };
            var tabStruct = new TabPage("STRUCTURE") { BackColor = Color.White };
            var tabData = new TabPage("DATA") { BackColor = Color.White };

            tvJunkResults = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, DrawMode = TreeViewDrawMode.OwnerDrawText };
            tabStruct.Controls.Add(tvJunkResults);

            // tabData: SplitContainer (grid top / detail bottom)
            _splitJunkData = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 320,
                FixedPanel = FixedPanel.Panel2,
                BackColor = Color.White
            };

            dgvJunkDataResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                Name = "dgvJunkDataResults",
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                ColumnHeadersHeight = 32
            };
            StyleDataGridView(dgvJunkDataResults);
            dgvJunkDataResults.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvJunkDataResults.GridColor = Color.FromArgb(230, 230, 240);

            // Columns — remove Database (redundant), keep: Selected|TABLE+ROW|COL|PK|VALUE|CASCADE
            var chkColJunk = new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "✓", Width = 30, MinimumWidth = 30, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };
            dgvJunkDataResults.Columns.Add(chkColJunk);
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Database", HeaderText = "DB", Width = 50, MinimumWidth = 40, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Table", HeaderText = "TABLE", Width = 180, MinimumWidth = 120, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "COL", Width = 140, MinimumWidth = 80, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn { Name = "PK", HeaderText = "PK", Width = 240, MinimumWidth = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Reason", HeaderText = "REASON / VALUE",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 180
            });
            dgvJunkDataResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Cascade", HeaderText = "⚠ CASCADE",
                Width = 160, MinimumWidth = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) }
            });

            _splitJunkData.Panel1.Controls.Add(dgvJunkDataResults);

            // Detail panel (bottom)
            _pnlJunkDataDetail = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 248, 252) };
            _lblJunkDetailHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(30, 30, 46),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Padding = new Padding(10, 6, 0, 0),
                Text = "  📋  Select a record to see full details"
            };
            _rtbJunkDetail = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(247, 248, 252),
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _pnlJunkDataDetail.Controls.Add(_rtbJunkDetail);
            _pnlJunkDataDetail.Controls.Add(_lblJunkDetailHeader);
            _splitJunkData.Panel2.Controls.Add(_pnlJunkDataDetail);

            tabData.Controls.Add(_splitJunkData);

            tcJunkResults.TabPages.AddRange(new TabPage[] { tabStruct, tabData });
            
            cardResults.Controls.Add(tcJunkResults);
            cardResults.Controls.Add(pnlResultToolbar);
            tcJunkResults.BringToFront();

            pnlJunkContent.Controls.Add(pnlLeftSidebar, 0, 0);
            pnlJunkContent.Controls.Add(cardResults, 1, 0);

            pnlJunkRoot.Controls.Add(pnlJunkHeader, 0, 0);
            pnlJunkRoot.Controls.Add(pnlJunkContent, 0, 1);
            tabCleanJunk.Controls.Add(pnlJunkRoot);

            // Wire events
            btnSelectAllScope.Click += (s, e) => SetAllJunkSelectionCheckState(true);
            btnUnselectAllScope.Click += (s, e) => SetAllJunkSelectionCheckState(false);
            btnSelectAllResult.Click += (s, e) => SetAllJunkResultsCheckState(true);
            btnUnselectAllResult.Click += (s, e) => SetAllJunkResultsCheckState(false);
            tvJunkSelection.AfterCheck += TvJunkSelection_AfterCheck;
            tvJunkSelection.BeforeExpand += TvJunkSelection_BeforeExpand;
            btnAnalyzeJunk.Click += BtnAnalyzeJunk_Click;
            btnGenerateJunkScript.Click += BtnGenerateJunkScript_Click;
            btnCleanJunk.Click += BtnCleanJunk_Click;
            tvJunkResults.DrawNode += TvJunkResults_DrawNode;
            tvJunkResults.AfterCheck += TvJunkResults_AfterCheck;
            tvJunkResults.NodeMouseDoubleClick += TvJunkResults_NodeMouseDoubleClick;
            dgvJunkDataResults.CellDoubleClick += DgvJunkDataResults_CellDoubleClick;
            dgvJunkDataResults.CellPainting += DgvJunkDataResults_CellPainting;
            dgvJunkDataResults.CellClick += DgvJunkDataResults_CellClick;
            dgvJunkDataResults.SelectionChanged += DgvJunkDataResults_SelectionChanged;
            dgvJunkDataResults.CellMouseClick += DgvJunkDataResults_CellMouseClick;

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
            var tabFinalExport = new TabPage("7. Final Export") { BackColor = Color.White };
            var pnlFinalMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            var lblExportIntro = new Label {
                Text = "Generate final production-ready database backup and synchronization SQL scripts for the target environment.",
                Dock = DockStyle.Top, Height = 42,
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            var cardFinal = CreateCardPanel("FINAL DATABASE EXPORT", 800, 110);
            cardFinal.Dock = DockStyle.Top;
            var btnExportFinal = new Button { Text = "\uD83D\uDCE6  Generate Production Export (Backup + SQL)", Width = 400, Height = 40, Location = new Point(15, 52) };
            StyleButtonPrimary(btnExportFinal); btnExportFinal.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold);
            btnExportFinal.Click += BtnExportFinal_Click;
            cardFinal.Controls.Add(btnExportFinal);

            var spacerFinal1 = new Panel { Dock = DockStyle.Top, Height = 16 };

            var lblExportLogHeader = new Label {
                Text = "\uD83D\uDCDC  EXPORT LOG", Dock = DockStyle.Top, Height = 28,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var pnlExportLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtFinalExportLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtFinalExportLog);
            pnlExportLog.Controls.Add(txtFinalExportLog);

            // Responsive widths
            void UpdateExportWidths() {
                var w = Math.Max(400, pnlFinalMain.ClientSize.Width - pnlFinalMain.Padding.Horizontal);
                cardFinal.Width = w;
            }
            pnlFinalMain.SizeChanged += (s, e) => UpdateExportWidths();
            pnlFinalMain.HandleCreated += (s, e) => UpdateExportWidths();

            pnlFinalMain.Controls.Add(pnlExportLog);      // Fill (bottom priority, added first)
            pnlFinalMain.Controls.Add(lblExportLogHeader); // Top
            pnlFinalMain.Controls.Add(spacerFinal1);      // Spacer
            pnlFinalMain.Controls.Add(cardFinal);         // Top
            pnlFinalMain.Controls.Add(lblExportIntro);    // Top (topmost priority, added last)
            
            tabFinalExport.Controls.Add(pnlFinalMain);
            var tabConfigCompare = new TabPage("8. Config Compare") { BackColor = Color.White };
            var pnlConfigMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            // Intro label
            var lblConfigIntro = new Label {
                Text = "Compare configuration files between environments to detect setting differences before deployment.",
                Dock = DockStyle.Top, Height = 38,
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            // Card: Config file selection
            var cardConfigSetup = CreateCardPanel("ENVIRONMENT CONFIGURATION COMPARISON", 800, 220);
            cardConfigSetup.Dock = DockStyle.Top;
            var pnlConfigBody = new TableLayoutPanel {
                Location = new Point(0, 36), Width = 760, Height = 110,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 2, RowCount = 3, Padding = new Padding(5, 2, 5, 0)
            };
            pnlConfigBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlConfigBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlConfigBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); // labels
            pnlConfigBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // textboxes
            pnlConfigBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // buttons row

            var lblOldConfig = new Label { Text = "Source Config File:", Dock = DockStyle.Fill, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };
            var lblNewConfig = new Label { Text = "Target Config File:", Dock = DockStyle.Fill, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };
            txtOldConfigPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 6, 0), ReadOnly = true, BackColor = UIConstants.Surface };
            txtNewConfigPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 0), ReadOnly = true, BackColor = UIConstants.Surface };
            btnSelectOldConfig = new Button { Text = "\uD83D\uDCC1  Browse Source Config", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 0) };
            StyleButtonSecondary(btnSelectOldConfig); btnSelectOldConfig.Font = new Font(UIConstants.MainFontName, 8.5f);
            btnSelectNewConfig = new Button { Text = "\uD83D\uDCC1  Browse Target Config", Dock = DockStyle.Fill, Margin = new Padding(6, 4, 0, 0) };
            StyleButtonSecondary(btnSelectNewConfig); btnSelectNewConfig.Font = new Font(UIConstants.MainFontName, 8.5f);

            pnlConfigBody.Controls.Add(lblOldConfig, 0, 0);
            pnlConfigBody.Controls.Add(lblNewConfig, 1, 0);
            pnlConfigBody.Controls.Add(txtOldConfigPath, 0, 1);
            pnlConfigBody.Controls.Add(txtNewConfigPath, 1, 1);
            pnlConfigBody.Controls.Add(btnSelectOldConfig, 0, 2);
            pnlConfigBody.Controls.Add(btnSelectNewConfig, 1, 2);

            btnCompareConfig = new Button { Text = "\u2194  Analyze Configuration Differences", Width = 300, Height = 36, Location = new Point(15, 170) };
            StyleButtonPrimary(btnCompareConfig); btnCompareConfig.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            pnlConfigBody.Location = new Point(15, 52); // Fix title overlap and add left margin

            cardConfigSetup.Controls.Add(pnlConfigBody);
            cardConfigSetup.Controls.Add(btnCompareConfig);

            // Responsive
            void UpdateConfigWidths() {
                var w = Math.Max(400, pnlConfigMain.ClientSize.Width - pnlConfigMain.Padding.Horizontal);
                cardConfigSetup.Width = w;
                pnlConfigBody.Width = Math.Max(200, cardConfigSetup.ClientSize.Width - 30);
                btnCompareConfig.Location = new Point(15, 170);
            }
            pnlConfigMain.SizeChanged += (s, e) => UpdateConfigWidths();
            pnlConfigMain.HandleCreated += (s, e) => UpdateConfigWidths();
            cardConfigSetup.SizeChanged += (s, e) => pnlConfigBody.Width = Math.Max(200, cardConfigSetup.ClientSize.Width);

            // Log section
            var spacerConfig1 = new Panel { Dock = DockStyle.Top, Height = 16 };
            var lblConfigLogHeader = new Label {
                Text = "\uD83D\uDCDC  CONFIGURATION DIFF LOG", Dock = DockStyle.Top, Height = 28,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var pnlConfigLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtConfigDiffLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtConfigDiffLog);
            pnlConfigLog.Controls.Add(txtConfigDiffLog);

            pnlConfigMain.Controls.Add(pnlConfigLog);         // Fill (bottom priority, added first)
            pnlConfigMain.Controls.Add(lblConfigLogHeader);   // Top
            pnlConfigMain.Controls.Add(spacerConfig1);
            pnlConfigMain.Controls.Add(cardConfigSetup);      // Top
            pnlConfigMain.Controls.Add(lblConfigIntro);       // Top (topmost priority, added last)

            tabConfigCompare.Controls.Add(pnlConfigMain);
            btnSelectOldConfig.Click += (s, e) => SelectFile(txtOldConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
            btnSelectNewConfig.Click += (s, e) => SelectFile(txtNewConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
            btnCompareConfig.Click += BtnCompareConfig_Click;

            // 9. AI Review Tab
            var tabAi = new TabPage("9. AI Review") { BackColor = Color.White };
            var pnlAiMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            var lblAiIntro = new Label {
                Text = "Utilize AI models to audit and validate database schema changes and configuration differences for potential issues.",
                Dock = DockStyle.Top, Height = 42,
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            var cardAiActions = CreateCardPanel("AI-POWERED VALIDATION", 800, 120);
            cardAiActions.Dock = DockStyle.Top;
            var pnlAiBtns = new FlowLayoutPanel { Width = 750, Height = 50, FlowDirection = FlowDirection.LeftToRight, Location = new Point(15, 52) };
            
            btnReviewSchema = new Button { Text = "\u2728  Review Schema Changes", Width = 230, Height = 36, Margin = new Padding(0, 0, 10, 0) };
            StyleButtonPrimary(btnReviewSchema); btnReviewSchema.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            
            btnReviewConfig = new Button { Text = "\u2728  Audit Configuration Diff", Width = 230, Height = 36 };
            StyleButtonPrimary(btnReviewConfig); btnReviewConfig.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            
            btnReviewSchema.Click += BtnReviewSchema_Click;
            btnReviewConfig.Click += BtnReviewConfig_Click;

            pnlAiBtns.Controls.Add(btnReviewSchema);
            pnlAiBtns.Controls.Add(btnReviewConfig);
            cardAiActions.Controls.Add(pnlAiBtns);

            var spacerAi1 = new Panel { Dock = DockStyle.Top, Height = 16 };
            var lblAiLogHeader = new Label {
                Text = "\uD83D\u2728  AI AUDIT LOG", Dock = DockStyle.Top, Height = 28,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var pnlAiLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtAiReviewLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtAiReviewLog);
            pnlAiLog.Controls.Add(txtAiReviewLog);

            // Responsive widths
            void UpdateAiWidths() {
                var w = Math.Max(400, pnlAiMain.ClientSize.Width - pnlAiMain.Padding.Horizontal);
                cardAiActions.Width = w;
                pnlAiBtns.Width = Math.Max(200, w - 30);
            }
            pnlAiMain.SizeChanged += (s, e) => UpdateAiWidths();
            pnlAiMain.HandleCreated += (s, e) => UpdateAiWidths();

            pnlAiMain.Controls.Add(pnlAiLog);          // Fill (bottom priority, added first)
            pnlAiMain.Controls.Add(lblAiLogHeader);    // Top
            pnlAiMain.Controls.Add(spacerAi1);
            pnlAiMain.Controls.Add(cardAiActions);     // Top
            pnlAiMain.Controls.Add(lblAiIntro);         // Top (topmost priority, added last)
            
            tabAi.Controls.Add(pnlAiMain);
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


        private void TreeSchema_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;

            // Root nodes (Category names)
            if (e.Node.Parent == null) 
            {
                Color rootColor = UIConstants.Primary;
                Font boldFont = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
                
                Rectangle rootBounds = e.Bounds;
                rootBounds.X += 4;
                rootBounds.Width += 50;
                
                if ((e.State & TreeNodeStates.Selected) != 0)
                {
                    using (var b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                        e.Graphics.FillRectangle(b, rootBounds);
                }
                else
                {
                    e.Graphics.FillRectangle(Brushes.White, rootBounds);
                }

                TextRenderer.DrawText(e.Graphics, e.Node.Text, boldFont, rootBounds, rootColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                boldFont.Dispose();
                return;
            }

            // Object nodes
            string icon = UIConstants.IconDatabase;
            Color iconColor = UIConstants.TextSecondary;
            
            string parentText = e.Node.Parent.Text;
            switch (parentText)
            {
                case "Tables": icon = UIConstants.IconTable; iconColor = Color.FromArgb(0, 120, 212); break;
                case "Views": icon = UIConstants.IconView; iconColor = Color.FromArgb(104, 33, 122); break;
                case "Functions": icon = UIConstants.IconFunction; iconColor = Color.FromArgb(16, 124, 16); break;
                case "Indexes": icon = UIConstants.IconIndex; iconColor = Color.FromArgb(216, 59, 1); break;
                case "Triggers": icon = UIConstants.IconTrigger; iconColor = Color.FromArgb(255, 140, 0); break;
                case "Extensions": icon = UIConstants.IconExtension; iconColor = Color.FromArgb(0, 153, 153); break;
                case "Roles": icon = UIConstants.IconRole; iconColor = Color.FromArgb(102, 102, 102); break;
                case "Sequences": icon = UIConstants.IconSequence; iconColor = Color.FromArgb(153, 0, 153); break;
            }

            // Detect status
            string pureText = e.Node.Text;
            Color statusColor = Color.Transparent;
            string statusTag = "";

            if (pureText.Contains("[NEW]")) { statusColor = UIConstants.Success; statusTag = "NEW"; pureText = pureText.Replace("[NEW] ", ""); }
            else if (pureText.Contains("[DIFF]")) { statusColor = UIConstants.Primary; statusTag = "DIFF"; pureText = pureText.Replace("[DIFF] ", ""); }
            else if (pureText.Contains("[REMOVED]")) { statusColor = UIConstants.Danger; statusTag = "REMOVED"; pureText = pureText.Replace("[REMOVED] ", ""); }

            Rectangle drawBounds = e.Bounds;
            drawBounds.X += 6;
            drawBounds.Width += 120; // Room for status pill

            // Selection
            if ((e.State & TreeNodeStates.Selected) != 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(232, 242, 252)))
                    e.Graphics.FillRectangle(b, drawBounds);
            }
            else
            {
                e.Graphics.FillRectangle(Brushes.White, drawBounds);
            }

            // 1. Icon
            using (Font iconFont = new Font(UIConstants.IconFontName, 9f))
                TextRenderer.DrawText(e.Graphics, icon, iconFont, new Rectangle(drawBounds.X, drawBounds.Y, 22, drawBounds.Height), iconColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            
            // 2. Text
            Font textFont = e.Node.TreeView?.Font ?? this.Font;
            Size textSize = TextRenderer.MeasureText(pureText, textFont);
            TextRenderer.DrawText(e.Graphics, pureText, textFont, new Rectangle(drawBounds.X + 24, drawBounds.Y, textSize.Width + 5, drawBounds.Height), UIConstants.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            // 3. Status Pill
            if (!string.IsNullOrEmpty(statusTag))
            {
                int tagX = drawBounds.X + 24 + textSize.Width + 12;
                using (Font tagFont = new Font(UIConstants.MainFontName, 7f, FontStyle.Bold))
                {
                    Size tagSize = TextRenderer.MeasureText(statusTag, tagFont);
                    Rectangle tagRect = new Rectangle(tagX, drawBounds.Y + 4, tagSize.Width + 10, drawBounds.Height - 8);
                    
                    int r = tagRect.Height - 1;
                    if (r > 0 && tagRect.Width > r)
                    {
                        using (var gp = new System.Drawing.Drawing2D.GraphicsPath()) {
                            gp.AddArc(tagRect.X, tagRect.Y, r, r, 90, 180);
                            gp.AddArc(tagRect.Right - r, tagRect.Y, r, r, 270, 180);
                            gp.CloseFigure();
                            using (var b = new SolidBrush(Color.FromArgb(40, statusColor)))
                                e.Graphics.FillPath(b, gp);
                            using (var p = new Pen(statusColor, 1))
                                e.Graphics.DrawPath(p, gp);
                        }
                    }
                    else if (tagRect.Width > 0 && tagRect.Height > 0)
                    {
                        // Fallback to simple rectangle if too small for arcs
                        using (var b = new SolidBrush(Color.FromArgb(40, statusColor)))
                            e.Graphics.FillRectangle(b, tagRect);
                        using (var p = new Pen(statusColor, 1))
                            e.Graphics.DrawRectangle(p, tagRect);
                    }
                    TextRenderer.DrawText(e.Graphics, statusTag, tagFont, tagRect, statusColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(t)) return false;
            string s1 = s.Trim().TrimEnd(',');
            string t1 = t.Trim().TrimEnd(',');
            if (s1 == t1) return true;

            // 1. Both start with quoted identifier
            if (s1.StartsWith("\"") && t1.StartsWith("\"")) {
                int sEnd = s1.IndexOf("\"", 1);
                int tEnd = t1.IndexOf("\"", 1);
                if (sEnd > 0 && tEnd > 0 && s1.Substring(0, sEnd) == t1.Substring(0, tEnd))
                    return true;
            }

            // 2. Both start with same Word/Identifier (unquoted)
            var sWords = s1.Split(new[] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);
            var tWords = t1.Split(new[] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);
            if (sWords.Length > 0 && tWords.Length > 0 && sWords[0] == tWords[0])
                return true;

            // 3. Significant common prefix (starts-with)
            int minLen = Math.Min(s1.Length, t1.Length);
            if (minLen > 8) {
                int commonCount = 0;
                while (commonCount < minLen && s1[commonCount] == t1[commonCount]) commonCount++;
                if (commonCount > minLen / 2) return true;
            }

            return false;
        }

        private void UpdateDiffView(string source, string target)
        {
            txtSourceDdl.Clear();
            txtTargetDdl.Clear();
            txtSourceLineNumbers.Clear();
            txtTargetLineNumbers.Clear();

            int sLineNum = 1;
            int tLineNum = 1;

            var sLines = (source ?? "").Trim().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var tLines = (target ?? "").Trim().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int m = sLines.Length;
            int n = tLines.Length;

            var diffRows = new List<(string? s, string? t, Color sCol, Color tCol, bool isPair)>();

            // Intercept purely synthetic placeholder messages for missing/removed objects
            bool targetDummy = n == 1 && (tLines[0].StartsWith("-- Object") || tLines[0].StartsWith("-- N/A"));
            bool sourceDummy = m == 1 && (sLines[0].StartsWith("-- Object") || sLines[0].StartsWith("-- N/A"));

            if (targetDummy || sourceDummy)
            {
                // Force a perfectly zipped row alignment to prevent blank spaces
                int maxLines = Math.Max(m, n);
                for (int idx = maxLines - 1; idx >= 0; idx--)
                {
                    string? sL = idx < m ? sLines[idx] : null;
                    string? tL = idx < n ? tLines[idx] : null;
                    
                    Color sCol = Color.White;
                    Color tCol = Color.White;
                    
                    if (targetDummy) {
                        sCol = sL != null ? Color.FromArgb(232, 252, 232) : Color.White;
                        tCol = tL != null ? Color.FromArgb(255, 230, 230) : Color.White;
                    } else if (sourceDummy) {
                        sCol = sL != null ? Color.FromArgb(255, 230, 230) : Color.White;
                        tCol = tL != null ? Color.FromArgb(232, 252, 232) : Color.White;
                    }
                    
                    diffRows.Add((sL, tL, sCol, tCol, false));
                }
            }
            else
            {
                // Compute LCS for alignment for normal comparisons
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
                    else if (i > 0 && (j == 0 || lcs[i - 1, j] >= lcs[i, j - 1]))
                    {
                        // Source only (New) -> Light Green
                        diffRows.Add((sLines[i - 1], null, Color.FromArgb(232, 252, 232), Color.White, false));
                        i--;
                    }
                    else
                    {
                        // Target only (Old) -> Light Red
                        diffRows.Add((null, tLines[j - 1], Color.White, Color.FromArgb(255, 230, 230), false));
                        j--;
                    }
                }
            }
            diffRows.Reverse();

            foreach (var row in diffRows)
            {
                AppendDiffLine(txtSourceDdl, txtSourceLineNumbers, row.s, row.sCol, ref sLineNum, row.isPair ? row.t : null);
                AppendDiffLine(txtTargetDdl, txtTargetLineNumbers, row.t, row.tCol, ref tLineNum, row.isPair ? row.s : null);
            }

            HighlightSqlKeywords(txtSourceDdl);
            HighlightSqlKeywords(txtTargetDdl);
            
            // 3. Apply Intra-line diffs AFTER keywords to ensure visibility
            int curSIdx = 0, curTIdx = 0;
            foreach (var row in diffRows)
            {
                if (row.isPair && row.s != null && row.t != null)
                {
                    HighlightInLineDiff(txtSourceDdl, curSIdx, row.s, row.t);
                    HighlightInLineDiff(txtTargetDdl, curTIdx, row.t, row.s);
                }
                curSIdx += (row.s?.Length ?? 0) + 1;
                curTIdx += (row.t?.Length ?? 0) + 1;
            }

            // Auto-sync line number scroll after population
            SendMessage(txtSourceLineNumbers.Handle, WM_VSCROLL, 4, 0); 
            SendMessage(txtTargetLineNumbers.Handle, WM_VSCROLL, 4, 0);
        }

        private void AppendDiffLine(RichTextBox rtb, RichTextBox gutter, string? text, Color backColor, ref int lineNum, string? otherText = null)
        {
            int start = rtb.TextLength;
            string displayText = (text ?? "");
            rtb.AppendText(displayText + "\n");
            
            // Append line number to gutter
            if (text != null)
                gutter.AppendText($"{lineNum++}\n");
            else
                gutter.AppendText("~\n"); // Placeholder for gaps
            int end = rtb.TextLength;
            if (displayText == "~")
            {
                rtb.Select(start, end - start);
                rtb.SelectionColor = Color.FromArgb(200, 200, 200); // Light gray for markers
                rtb.DeselectAll();
                return;
            }

            rtb.Select(start, end - start);
            rtb.SelectionBackColor = backColor;
            
            if (displayText.StartsWith("-- Object")) {
                rtb.SelectionColor = Color.Gray;
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Italic);
            }
            
            if (backColor != Color.White)
            {
                rtb.SelectionColor = Color.FromArgb(40, 40, 60);
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
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
                rtb.SelectionColor = Color.DarkRed; // High contrast
                rtb.SelectionBackColor = Color.FromArgb(255, 255, 180); // Light Yellow highlight
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

                pnlStatusLabels.Controls.Clear();

                // Update dynamic headers with technical connection info
                lblSourceDdlHeader.Text = $"SOURCE: {cmbSourceDb.Text} ({_newDbConfig.Host}:{_newDbConfig.Port})";
                lblTargetDdlHeader.Text = $"TARGET: {cmbTargetDb.Text} ({_oldDbConfig.Host}:{_oldDbConfig.Port})";

                AddStatusBadge($"Comparing {sourceSchema} vs {targetSchema}...", UIConstants.Primary);
                _schemaDiffs = await _dbCompareService!.GenerateSchemaDiffResultsAsync(sourceSchema, targetSchema);

                var oldTablesRes = await _oldPgService!.GetSchemaTablesAsync(_oldDbConfig!.DatabaseName!, targetSchema);
                var oldTables = oldTablesRes.Select(t => t.Name).ToList();
                var newTablesRes = await _newPgService!.GetSchemaTablesAsync(_newDbConfig!.DatabaseName!, sourceSchema);
                var newTables = newTablesRes.Select(t => t.Name).ToList();
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

                pnlStatusLabels.Controls.Clear();
                AddStatusBadge($"{_schemaDiffs.Count} Differences", UIConstants.Primary);

                var typeCounts = _schemaDiffs.GroupBy(d => d.ObjectType)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                foreach (var g in typeCounts)
                {
                    string label = $"{g.Count()} {g.Key}{(g.Count() > 1 ? (g.Key == "Index" ? "es" : "s") : "")}";
                    AddStatusBadge(label, UIConstants.TextSecondary);
                }
            }
            catch (Exception ex)
            {
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Error", UIConstants.Danger);
                txtBackupLog.AppendText($"\r\nDiff Error: {ex.Message}");
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

                var selectedDiffs = GetCheckedSchemaDiffs(treeSchema.Nodes);
                if (!selectedDiffs.Any())
                {
                    pnlStatusLabels.Controls.Clear();
                    AddStatusBadge("Please select items first", UIConstants.Warning);
                    MessageBox.Show("Please select at least one object in the tree view to generate the migration script.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var r in selectedDiffs)
                {
                    sb.AppendLine($"-- {r.ObjectType}: {r.ObjectName} ({r.DiffType})");
                    sb.AppendLine(r.DiffScript);
                    sb.AppendLine();
                }

                var sql = sb.ToString();
                var path = _fileSystemService!.GetSqlScriptPath(NewDbName, true);
                _fileSystemService!.WriteToFile(path, sql);
                pnlStatusLabels.Controls.Clear();
                
                string fileName = System.IO.Path.GetFileName(path);
                AddStatusBadge($"{UIConstants.IconCheck}  Exported: {fileName}", UIConstants.Success, () => {
                    try {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path.Replace("/", "\\")}\"");
                    } catch { }
                });

                AddStatusBadge($"{UIConstants.IconRobot}  Review with AI", UIConstants.Primary, () => {
                    tabControl.SelectedIndex = 8; // Switch to AI Review tab
                });

                _lastSchemaExportPath = path;
                btnOpenSchemaFolder.Visible = true;
                btnEditSchema.Visible = true;
            }
            catch (Exception ex)
            {
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Export Error", UIConstants.Danger);
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

            }
            catch (Exception ex) {
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge($"DB Load Error: {ex.Message}", UIConstants.Danger);
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
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Schema Load Error", UIConstants.Danger);
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
                var sourceTablesRes = await _newPgService!.GetSchemaTablesAsync(_newDbConfig.DatabaseName, sourceSchema);
                var sourceTables = sourceTablesRes.Select(t => t.Name).ToList();
                var targetTablesRes = await _oldPgService!.GetSchemaTablesAsync(_oldDbConfig.DatabaseName, targetSchema);
                var targetTables = targetTablesRes.Select(t => t.Name).ToList();
                
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
                        var count = await _newPgService!.GetTableRowCountAsync(_newDbConfig.DatabaseName, table, sourceSchema);
                        row.DefaultCellStyle.BackColor = Color.FromArgb(230, 240, 255); // Light Blue (Added)
                        row.Cells["ColIdentical"].Value = "Added (New)";
                        row.Cells["ColSource"].Value = count;
                        row.Cells["ColCheck"].Value = false;
                        row.Cells["ColCheck"].ReadOnly = true;
                    }
                    else if (!inSource && inTarget)
                    {
                        var count = await _oldPgService!.GetTableRowCountAsync(_oldDbConfig.DatabaseName, table, targetSchema);
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
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge($"Load Error: {ex.Message}", UIConstants.Danger);
                lblDataStatus.Text = "❌ Error loading tables.";
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

            if (!checkedRows.Any()) { 
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Select tables first", UIConstants.Warning);
                return; 
            }

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
            catch (Exception ex) {
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge($"Table Load Error: {ex.Message}", UIConstants.Danger);
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
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Detail Load Error", UIConstants.Danger);
                Console.WriteLine(ex.ToString());
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
            if (!tablesToSync.Any()) { 
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Check tables first", UIConstants.Warning);
                return; 
            }

            if (!EnsureServicesInitialized()) return;

            string sourceSchema = cmbSourceDataSchema.Text;
            string targetSchema = cmbTargetDataSchema.Text;
            
            lblDataStatus.Text = "⌛ Generating synchronization script...";
            this.Cursor = Cursors.WaitCursor;
            pbDataLoading.Visible = true;
            
            try
            {
                var options = GetDataCompareOptions();
                var diffScript = await _dbCompareService!.GenerateDataDiffAsync(tablesToSync, sourceSchema, targetSchema, options);
                
                var fileName = $"data_sync_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                var fullPath = _fileSystemService!.SaveSqlScript(fileName, diffScript, false);
                _lastDataExportPath = fullPath;
                
                pnlStatusLabels.Controls.Clear();
                string fileNameOnly = System.IO.Path.GetFileName(fullPath);
                
                AddStatusBadge($"{UIConstants.IconCheck}  Exported: {fileNameOnly}", UIConstants.Success, () => {
                    if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                        Process.Start("explorer.exe", $"/select,\"{fullPath.Replace("/", "\\")}\"");
                });

                AddStatusBadge($"{UIConstants.IconRobot}  Review with AI", UIConstants.Primary, () => {
                    tabControl.SelectedIndex = 8; // Switch to AI Review tab
                });

                btnOpenDataFolder.Visible = true;
                btnEditData.Visible = true;
                lblDataStatus.Text = $"✅ Exported to {fileNameOnly}";

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating data sync script:\n{ex.Message}");
            }
            finally
            {
                this.Cursor = Cursors.Default;
                pbDataLoading.Visible = false;
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
            } catch (Exception ex) { 
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("DB List Error", UIConstants.Danger); 
            }
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
                    var schemasRes = await service.GetSchemasAsync(dbName);
                    var schemas = schemasRes.Select(s => s.Name).ToList();
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
                             var schemasRes = await service.GetSchemasAsync(dbNode.Text);
                             var schemas = schemasRes.Select(s => s.Name).ToList();
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
                    // 0. Check for errors
                    if (res.Errors.Any())
                    {
                        var errorNode = new TreeNode($"⚠️ Errors in {res.DatabaseName}") { ForeColor = Color.Red };
                        foreach (var err in res.Errors) errorNode.Nodes.Add(new TreeNode(err));
                        tvJunkResults.Nodes.Add(errorNode);
                    }

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

                            // Add Dependent Objects (Cascade Impact)
                            if (item.DependentObjects.Any())
                            {
                                AddDependentNodes(itemNode, item);
                                itemNode.Expand(); // Show impacts by default
                            }
                        }
                        dbNode.Expand();
                        tvJunkResults.Nodes.Add(dbNode);
                    }

                    // 2. Populate Data Grid — Grouped by TABLE, sorted
                    var dataItems = res.Items.Where(i => i.Type == JunkType.DataRecord).ToList();
                    
                    // Deduplicate by PK (1 record may hit multiple columns)
                    var deduped = dataItems
                        .GroupBy(i => new { i.DatabaseName, i.SchemaName, i.ObjectName, i.PrimaryKeyValue })
                        .Select(g => {
                            var first = g.First();
                            // Merge all column names + raw values
                            first.ColumnName = string.Join(", ", g.Select(x => x.ColumnName).Distinct());
                            return first;
                        })
                        .OrderBy(i => i.ObjectName)   // group by table
                        .ThenBy(i => i.ColumnName)
                        .ThenBy(i => i.PrimaryKeyValue)
                        .ToList();

                    // Group by TABLE to insert group header rows
                    var byTable = deduped
                        .GroupBy(i => new { i.DatabaseName, i.SchemaName, i.ObjectName })
                        .OrderBy(g => g.Key.ObjectName);

                    foreach (var tableGroup in byTable)
                    {
                        int recCount = tableGroup.Count();
                        bool anyHasCascade = tableGroup.Any(i => i.DependentObjects.Any());

                        string groupCascadeText = anyHasCascade ? "⚠  Has cascade" : "";

                        // ── GROUP HEADER ROW ──────────────────────────────────────
                        int groupRowIdx = dgvJunkDataResults.Rows.Add(
                            true,                              // Selected
                            tableGroup.Key.DatabaseName,       // DB
                            $"▶  {tableGroup.Key.ObjectName}", // Table (starts collapsed)
                            $"{recCount} record(s)",           // COL
                            "",                               // PK
                            "",                               // VALUE
                            groupCascadeText                   // CASCADE
                        );
                        var groupRow = dgvJunkDataResults.Rows[groupRowIdx];
                        groupRow.Tag = $"GROUP:{tableGroup.Key.DatabaseName}.{tableGroup.Key.SchemaName}.{tableGroup.Key.ObjectName}";
                        groupRow.Height = 28;
                        groupRow.DefaultCellStyle.BackColor = Color.FromArgb(28, 40, 70);
                        groupRow.DefaultCellStyle.ForeColor = Color.White;
                        groupRow.DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                        groupRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 70, 110);
                        groupRow.DefaultCellStyle.SelectionForeColor = Color.White;
                        groupRow.Cells["Database"].Style.ForeColor = Color.FromArgb(140, 180, 255);
                        groupRow.Cells["Table"].Style.ForeColor = Color.White;
                        groupRow.Cells["Cascade"].Style.ForeColor = anyHasCascade ? Color.FromArgb(255, 200, 80) : Color.FromArgb(100, 140, 200);
                        // Make checkbox background match
                        groupRow.Cells["Selected"].Style.BackColor = Color.FromArgb(28, 40, 70);
                        groupRow.Cells["Column"].Style.ForeColor = Color.FromArgb(160, 200, 255);

                        // ── DATA ROWS for this table ──────────────────────────────
                        foreach (var item in tableGroup)
                        {
                            string aggVals = item.RawData?.Replace("\n", " ")?.Replace("\r", "") ?? "";
                            if (aggVals.Length > 160) aggVals = aggVals.Substring(0, 157) + "…";

                            // CASCADE summary text
                            string cascadeText;
                            if (item.DependentObjects.Any())
                            {
                                int affectedTables = item.DependentObjects.Count;
                                long totalAffectedRows = 0;
                                foreach (var dep in item.DependentObjects)
                                {
                                    var m = System.Text.RegularExpressions.Regex.Match(dep.DetectedContent ?? "", @"(\d+)\s+rows?");
                                    totalAffectedRows += m.Success && long.TryParse(m.Groups[1].Value, out long r) ? r : 1;
                                }
                                cascadeText = $"▶  {affectedTables}t × {totalAffectedRows}r";
                            }
                            else
                            {
                                cascadeText = "— none";
                            }

                            int rowIdx = dgvJunkDataResults.Rows.Add(
                                true,
                                item.DatabaseName,
                                item.ObjectName,
                                item.ColumnName,
                                item.PrimaryKeyValue,
                                aggVals,
                                cascadeText
                            );
                            var dataRow = dgvJunkDataResults.Rows[rowIdx];
                            dataRow.Tag = item;
                            dataRow.Height = 24;
                            dataRow.Visible = false; // Collapsed by default

                            if (item.DependentObjects.Any())
                            {
                                // Orange tint — has cascade impact
                                dataRow.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 230);
                                dataRow.Cells["Cascade"].Style.ForeColor = Color.DarkOrange;
                                dataRow.Cells["Cascade"].Style.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                            }
                            else
                            {
                                dataRow.Cells["Cascade"].Style.ForeColor = Color.FromArgb(160, 160, 175);
                            }

                            // ── CASCADE SUB-ROWS (hidden by default, toggle on click) ──
                            foreach (var dep in item.DependentObjects)
                            {
                                var countMatch = System.Text.RegularExpressions.Regex.Match(dep.DetectedContent ?? "", @"(\d+)\s+rows?");
                                string depRows = countMatch.Success ? $"{countMatch.Groups[1].Value} row(s)" : "?";

                                int subIdx = dgvJunkDataResults.Rows.Add(
                                    false,                // not selectable for delete
                                    "",
                                    $"    └─ {dep.SchemaName}.{dep.ObjectName}",
                                    $"via FK",
                                    "",
                                    dep.DetectedContent ?? "",
                                    depRows
                                );
                                var subRow = dgvJunkDataResults.Rows[subIdx];
                                subRow.Tag = $"CASCADE_SUB:{rowIdx}";   // parent row index
                                subRow.Visible = false;                  // collapsed by default
                                subRow.Height = 22;
                                subRow.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 205);
                                subRow.DefaultCellStyle.ForeColor = Color.FromArgb(130, 80, 0);
                                subRow.DefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
                                subRow.Cells["Table"].Style.ForeColor = Color.DarkOrange;
                                subRow.Cells["Selected"].Style.BackColor = Color.FromArgb(255, 243, 205);
                            }
                        }
                    }

                }
                
                if (!_lastJunkResults.Any() || !_lastJunkResults.Any(r => r.Items.Any() || r.Errors.Any())) 
                {
                    MessageBox.Show("No junk found with these keywords and selections!");
                }
                else
                {
                    // Update: Automatically switch to DATA tab if only data was found
                    bool hasStructural = tvJunkResults.Nodes.Cast<TreeNode>().Any(n => !n.Text.StartsWith("⚠️"));
                    if (dgvJunkDataResults.Rows.Count > 0 && !hasStructural)
                    {
                        tcJunkResults.SelectedIndex = 1;
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Analysis failed: " + ex.Message); }
            finally {
                btnAnalyzeJunk.Enabled = true;
                btnAnalyzeJunk.Text = "🔍 ANALYZE JUNK";
            }
        }

        // ── Toggle CASCADE sub-rows when clicking the CASCADE cell (▶) ──────────
        private void DgvJunkDataResults_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = dgvJunkDataResults.Rows[e.RowIndex];

            // Toggle cascade sub-rows when clicking CASCADE column on a DATA row with dependents
            if (e.ColumnIndex == dgvJunkDataResults.Columns["Cascade"]?.Index
                && row.Tag is JunkItem jItem && jItem.DependentObjects.Any())
            {
                ToggleCascadeSubRows(e.RowIndex, jItem);
            }
        }

        private void ToggleCascadeSubRows(int parentRowIdx, JunkItem item)
        {
            dgvJunkDataResults.SuspendLayout();
            try
            {
                // Find all sub-rows immediately following this row tagged with CASCADE_SUB:{parentRowIdx}
                bool anyVisible = false;
                for (int i = parentRowIdx + 1; i < dgvJunkDataResults.Rows.Count; i++)
                {
                    var r = dgvJunkDataResults.Rows[i];
                    if (r.Tag is string tag && tag == $"CASCADE_SUB:{parentRowIdx}")
                        anyVisible = anyVisible || r.Visible;
                    else if (!(r.Tag is string s2 && s2.StartsWith("CASCADE_SUB:")))
                        break; // hit a non-sub row, stop
                }

                bool newVisible = !anyVisible;
                string arrow = newVisible ? "▼" : "▶";
                var cascadeCell = dgvJunkDataResults.Rows[parentRowIdx].Cells["Cascade"];

                // Update arrow and toggle visibility
                string cascadeVal = cascadeCell.Value?.ToString() ?? "";
                // Replace ▶ or ▼ prefix
                cascadeVal = cascadeVal.Replace("▶", "").Replace("▼", "").TrimStart();
                cascadeCell.Value = $"{arrow}  {cascadeVal}";

                for (int i = parentRowIdx + 1; i < dgvJunkDataResults.Rows.Count; i++)
                {
                    var r = dgvJunkDataResults.Rows[i];
                    if (r.Tag is string tag && tag == $"CASCADE_SUB:{parentRowIdx}")
                        r.Visible = newVisible;
                    else if (!(r.Tag is string s2 && s2.StartsWith("CASCADE_SUB:")))
                        break;
                }
            }
            finally
            {
                dgvJunkDataResults.ResumeLayout();
            }
        }

        // ── Click on GROUP header row → toggle check all data rows in group ──────
        // ── Click on GROUP header row → toggle expand or check all ──────
        private void DgvJunkDataResults_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = dgvJunkDataResults.Rows[e.RowIndex];
            if (row.Tag is not string groupTag || !groupTag.StartsWith("GROUP:")) return;

            // 1. If clicked on the checkbox column, toggle checking all data rows
            if (e.ColumnIndex == dgvJunkDataResults.Columns["Selected"]?.Index)
            {
                bool newCheck = !(row.Cells["Selected"].Value is bool b && b);
                row.Cells["Selected"].Value = newCheck;

                for (int i = e.RowIndex + 1; i < dgvJunkDataResults.Rows.Count; i++)
                {
                    var r = dgvJunkDataResults.Rows[i];
                    if (r.Tag is string tag && tag.StartsWith("GROUP:")) break; // next group
                    if (r.Tag is JunkItem) r.Cells["Selected"].Value = newCheck;
                }
                dgvJunkDataResults.EndEdit();
            }
            // 2. Otherwise toggle expand/collapse visibility of data rows
            else
            {
                dgvJunkDataResults.SuspendLayout();
                try
                {
                    string oldTableVal = row.Cells["Table"].Value?.ToString() ?? "";
                    bool isCollapsed = oldTableVal.StartsWith("▶");
                    string newArrow = isCollapsed ? "▼" : "▶";
                    row.Cells["Table"].Value = newArrow + oldTableVal.Substring(1);

                    for (int i = e.RowIndex + 1; i < dgvJunkDataResults.Rows.Count; i++)
                    {
                        var r = dgvJunkDataResults.Rows[i];
                        if (r.Tag is string tag && tag.StartsWith("GROUP:")) break; // next group
                        
                        // If it's a data row, toggle visibility
                        if (r.Tag is JunkItem)
                        {
                            r.Visible = isCollapsed;
                            // Reset cascade sub-rows to hidden if collapsing, or keeping hidden if expanding
                        }
                        else if (r.Tag is string subtag && subtag.StartsWith("CASCADE_SUB:"))
                        {
                            r.Visible = false; // Always hide sub-rows when toggling parent group
                        }
                    }
                    
                    // If expanding, reset all parent data row cascade arrows to ▶
                    if (isCollapsed)
                    {
                        for (int i = e.RowIndex + 1; i < dgvJunkDataResults.Rows.Count; i++)
                        {
                            var r = dgvJunkDataResults.Rows[i];
                            if (r.Tag is string tag && tag.StartsWith("GROUP:")) break;
                            if (r.Tag is JunkItem jItem && jItem.DependentObjects.Any())
                            {
                                string cascadeVal = r.Cells["Cascade"].Value?.ToString() ?? "";
                                if (cascadeVal.StartsWith("▼"))
                                    r.Cells["Cascade"].Value = "▶" + cascadeVal.Substring(1);
                            }
                        }
                    }
                }
                finally
                {
                    dgvJunkDataResults.ResumeLayout();
                }
            }
        }

        // ── SelectionChanged → load detail panel ────────────────────────────────
        private bool _detailLoading = false;
        private void DgvJunkDataResults_SelectionChanged(object? sender, EventArgs e)
        {
            if (_detailLoading) return;
            if (dgvJunkDataResults.SelectedRows.Count == 0) return;

            var selectedRow = dgvJunkDataResults.SelectedRows[0];
            if (selectedRow.Tag is JunkItem item)
            {
                _ = LoadDetailPanelAsync(item);
            }
            else if (selectedRow.Tag is string tag && tag.StartsWith("GROUP:"))
            {
                _lblJunkDetailHeader.Text = $"  📁  {tag.Replace("GROUP:", "")} — click a record row to see details";
                _rtbJunkDetail.Clear();
            }
        }

        private async Task LoadDetailPanelAsync(JunkItem item)
        {
            _detailLoading = true;
            try
            {
                _lblJunkDetailHeader.Text = $"  📋  {item.SchemaName}.{item.ObjectName}  ·  {item.PrimaryKeyColumn} = {item.PrimaryKeyValue}";
                _rtbJunkDetail.Clear();
                _rtbJunkDetail.Text = "⏳ Loading...";

                var pgSvc = GetJunkPostgresService();
                if (pgSvc == null || item.Type != JunkType.DataRecord
                    || string.IsNullOrEmpty(item.PrimaryKeyColumn)
                    || string.IsNullOrEmpty(item.PrimaryKeyValue))
                {
                    _rtbJunkDetail.Text = item.RawData ?? item.DetectedContent ?? "";
                    return;
                }

                try
                {
                    var fullRow = await pgSvc.GetFullRowDataAsync(
                        item.DatabaseName ?? "",
                        item.SchemaName ?? "public",
                        item.ObjectName ?? "",
                        item.PrimaryKeyColumn,
                        item.PrimaryKeyValue);

                    _rtbJunkDetail.Clear();

                    if (fullRow.Count == 0)
                    {
                        _rtbJunkDetail.Text = "(Record not found — may have been deleted)";
                        return;
                    }

                    int maxKeyLen = fullRow.Keys.Max(k => k.Length);
                    foreach (var kv in fullRow)
                    {
                        bool isJunk = kv.Key.Equals(item.ColumnName, StringComparison.OrdinalIgnoreCase)
                            || (item.ColumnName?.Split(',').Select(c => c.Trim()).Contains(kv.Key, StringComparer.OrdinalIgnoreCase) == true);
                        string pad = new string(' ', Math.Max(0, maxKeyLen - kv.Key.Length + 1));

                        int nameStart = _rtbJunkDetail.TextLength;
                        _rtbJunkDetail.AppendText(kv.Key + pad + " : ");
                        _rtbJunkDetail.Select(nameStart, kv.Key.Length);
                        _rtbJunkDetail.SelectionFont = new Font(_rtbJunkDetail.Font, FontStyle.Bold);
                        _rtbJunkDetail.SelectionColor = isJunk ? Color.DarkRed : Color.FromArgb(60, 100, 160);

                        int valStart = _rtbJunkDetail.TextLength;
                        _rtbJunkDetail.AppendText((kv.Value ?? "(null)") + "\n");

                        // Highlight junk keywords in value
                        if (isJunk && item.MatchedKeywords?.Any() == true)
                        {
                            string val = kv.Value ?? "";
                            foreach (var kw in item.MatchedKeywords)
                            {
                                if (string.IsNullOrEmpty(kw)) continue;
                                int idx = 0;
                                while ((idx = val.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                                {
                                    _rtbJunkDetail.Select(valStart + idx, kw.Length);
                                    _rtbJunkDetail.SelectionBackColor = Color.Yellow;
                                    _rtbJunkDetail.SelectionColor = Color.DarkRed;
                                    _rtbJunkDetail.SelectionFont = new Font(_rtbJunkDetail.Font, FontStyle.Bold);
                                    idx += kw.Length;
                                }
                            }
                        }
                    }
                    _rtbJunkDetail.DeselectAll();

                    // Append cascade section
                    if (item.DependentObjects.Any())
                    {
                        int sepStart = _rtbJunkDetail.TextLength;
                        _rtbJunkDetail.AppendText("\n\n⚠  CASCADE IMPACT — Deleting will also affect:\n");
                        _rtbJunkDetail.Select(sepStart, _rtbJunkDetail.TextLength - sepStart);
                        _rtbJunkDetail.SelectionColor = Color.DarkOrange;
                        _rtbJunkDetail.SelectionFont = new Font(_rtbJunkDetail.Font, FontStyle.Bold);
                        _rtbJunkDetail.DeselectAll();

                        foreach (var dep in item.DependentObjects)
                        {
                            int depStart = _rtbJunkDetail.TextLength;
                            _rtbJunkDetail.AppendText($"  • {dep.SchemaName}.{dep.ObjectName} — {dep.DetectedContent}\n");
                            _rtbJunkDetail.Select(depStart, _rtbJunkDetail.TextLength - depStart);
                            _rtbJunkDetail.SelectionColor = Color.FromArgb(160, 80, 0);
                        }
                        _rtbJunkDetail.DeselectAll();
                    }
                }
                catch (Exception ex)
                {
                    _rtbJunkDetail.Text = $"❌ Error loading detail: {ex.Message}";
                }
            }
            finally
            {
                _detailLoading = false;
            }
        }

        private void BtnGenerateJunkScript_Click(object? sender, EventArgs e)
        {
            if (_junkService == null || !_lastJunkResults.Any()) { MessageBox.Show("Please analyze junk first."); return; }
            var selectedItems = GetSelectedJunkItems();
            if (!selectedItems.Any()) { MessageBox.Show("No junk items selected."); return; }

            var script = _junkService.GenerateCleanupScript(selectedItems);
            
            using (var editor = new SqlScriptEditorForm(script, "Review & Edit Junk Cleanup Script", "Apply & Execute"))
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
                
                float currentX = e.CellBounds.X + e.CellStyle.Padding.Left + 4;
                
                Color foreColor = (e.State & DataGridViewElementStates.Selected) != 0 ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
                
                Region oldClip = e.Graphics.Clip;
                e.Graphics.SetClip(e.CellBounds);
                
                try
                {
                    using (var format = new StringFormat(StringFormat.GenericTypographic))
                    using (var brushFore = new SolidBrush(foreColor))
                    {
                        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                        float y = e.CellBounds.Y + (e.CellBounds.Height - e.CellStyle.Font.Height) / 2f;

                        foreach (var match in sortedMatches) {
                            if (match.start < currentPos) continue; 
                            
                            if (match.start > currentPos) {
                                string pre = text.Substring(currentPos, match.start - currentPos);
                                SizeF size = e.Graphics.MeasureString(pre, e.CellStyle.Font, 10000, format);
                                e.Graphics.DrawString(pre, e.CellStyle.Font, brushFore, currentX, y, format);
                                currentX += size.Width;
                            }
                            
                            string mid = text.Substring(match.start, match.length);
                            SizeF midSize = e.Graphics.MeasureString(mid, e.CellStyle.Font, 10000, format);
                            e.Graphics.FillRectangle(Brushes.Yellow, currentX, e.CellBounds.Y + 2, midSize.Width, e.CellBounds.Height - 4);
                            e.Graphics.DrawString(mid, e.CellStyle.Font, Brushes.Red, currentX, y, format);
                            currentX += midSize.Width;
                            currentPos = match.start + match.length;
                            
                            if (currentX > e.CellBounds.Right) break;
                        }
                        
                        if (currentPos < text.Length && currentX < e.CellBounds.Right) {
                            string post = text.Substring(currentPos);
                            e.Graphics.DrawString(post, e.CellStyle.Font, brushFore, currentX, y, format);
                        }
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
            // IMPORTANT: Must match GetActiveJunkPgService() mapping exactly
            if (cmbJunkConnection.SelectedIndex == 0) return _newPgService;   // Source (Dev)
            if (cmbJunkConnection.SelectedIndex == 1) return _oldPgService;   // Target (Prod)
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

        private void SetAllJunkResultsCheckState(bool check)
        {
            if (tcJunkResults.SelectedTab.Text == "STRUCTURE")
            {
                tvJunkResults.BeginUpdate();
                foreach (TreeNode node in tvJunkResults.Nodes)
                {
                    node.Checked = check;
                    SetNodeCheckStateRecursive(node, check);
                }
                tvJunkResults.EndUpdate();
            }
            else if (tcJunkResults.SelectedTab.Text == "DATA")
            {
                foreach (DataGridViewRow row in dgvJunkDataResults.Rows)
                {
                    // Only check actual JunkItem rows, not GROUP headers or CASCADE_SUB rows
                    if (row.Tag is JunkItem)
                        row.Cells["Selected"].Value = check;
                }
                dgvJunkDataResults.EndEdit();
            }
        }

        private void SetNodeCheckStateRecursive(TreeNode parentNode, bool check)
        {
            foreach (TreeNode node in parentNode.Nodes)
            {
                node.Checked = check;
                if (node.Nodes.Count > 0) SetNodeCheckStateRecursive(node, check);
            }
        }

        private void AddDependentNodes(TreeNode parentNode, JunkItem item)
        {
            foreach (var dep in item.DependentObjects)
            {
                var depNode = new TreeNode($"[CASCADE] {dep.SchemaName}.{dep.ObjectName}")
                {
                    Tag = dep,
                    Checked = true,
                    ToolTipText = dep.DetectedContent ?? "Cascade impact"
                };
                parentNode.Nodes.Add(depNode);
                
                if (dep.DependentObjects.Any())
                    AddDependentNodes(depNode, dep);
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

            if (e.Node.Tag is JunkItem ji && ji.IsCascadeImpact)
            {
                baseFont = new Font(baseFont, FontStyle.Italic);
                if (foreColor == Color.Empty) foreColor = Color.Gray;
            }

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
                float currentX = textRect.X;

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
                
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                using (var brushFore = new SolidBrush(foreColor))
                {
                    format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                    float y = textRect.Y + (textRect.Height - baseFont.Height) / 2f;

                    foreach (var match in sortedMatches) {
                        if (match.start < currentPos) continue; 
                        if (match.start > currentPos) {
                            string pre = text.Substring(currentPos, match.start - currentPos);
                            SizeF size = e.Graphics.MeasureString(pre, baseFont, 10000, format);
                            e.Graphics.DrawString(pre, baseFont, brushFore, currentX, y, format);
                            currentX += size.Width;
                        }
                        
                        string mid = text.Substring(match.start, match.length);
                        SizeF midSize = e.Graphics.MeasureString(mid, baseFont, 10000, format);
                        e.Graphics.FillRectangle(Brushes.Yellow, currentX, textRect.Y, midSize.Width, textRect.Height);
                        e.Graphics.DrawString(mid, baseFont, Brushes.Red, currentX, y, format);
                        currentX += midSize.Width;
                        currentPos = match.start + match.length;
                    }
                    
                    if (currentPos < text.Length) {
                        string post = text.Substring(currentPos);
                        e.Graphics.DrawString(post, baseFont, brushFore, currentX, y, format);
                    }
                }
            }
            else
            {
                // Container nodes (Database, Schema, Routine types) - Make them BOLD
                using (Font boldFont = new Font(baseFont, FontStyle.Bold))
                using (var brush = new SolidBrush(foreColor))
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    float y = textRect.Y + (textRect.Height - boldFont.Height) / 2f;
                    e.Graphics.DrawString(e.Node.Text, boldFont, brush, textRect.X, y, format);
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

            // 2. From Data Grid — skip GROUP header rows and CASCADE_SUB rows
            foreach (DataGridViewRow row in dgvJunkDataResults.Rows)
            {
                // Only include actual JunkItem rows (not GROUP headers or CASCADE_SUB rows)
                if (row.Tag is JunkItem dataItem
                    && row.Cells["Selected"].Value is bool b && b)
                {
                    list.Add(dataItem);
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
        private void AddStatusBadge(string text, Color color, Action? onClick = null)
        {
            var isHovered = false;
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                BackColor = Color.Transparent, 
                ForeColor = Color.White,
                Font = new Font(UIConstants.MainFontName, 8f, FontStyle.Bold),
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 2, 6, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 26
            };

            if (onClick != null)
            {
                lbl.Cursor = Cursors.Hand;
                lbl.Click += (s, e) => onClick();
                tooltip.SetToolTip(lbl, "Click to take action");
                
                lbl.MouseEnter += (s, e) => { isHovered = true; lbl.Invalidate(); };
                lbl.MouseLeave += (s, e) => { isHovered = false; lbl.Invalidate(); };
            }

            lbl.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                
                var drawColor = isHovered ? Color.FromArgb(Math.Min(255, color.R + 25), Math.Min(255, color.G + 25), Math.Min(255, color.B + 25)) : color;
                
                int radius = lbl.Height - 1;
                if (radius > 0 && lbl.Width > radius)
                {
                    using (var gp = new System.Drawing.Drawing2D.GraphicsPath()) {
                        gp.AddArc(0, 0, radius, radius, 90, 180);
                        gp.AddArc(lbl.Width - radius - 1, 0, radius, radius, 270, 180);
                        gp.CloseFigure();
                        
                        using (var b = new SolidBrush(drawColor)) {
                            g.FillPath(b, gp);
                        }
                        
                        // Subtle glow/border
                        using (var p = new Pen(Color.FromArgb(50, Color.White), 1)) {
                            g.DrawPath(p, gp);
                        }
                    }
                }
                else if (lbl.Width > 0 && lbl.Height > 0)
                {
                    using (var b = new SolidBrush(drawColor))
                        g.FillRectangle(b, lbl.ClientRectangle);
                }
                
                TextRenderer.DrawText(g, lbl.Text, lbl.Font, lbl.ClientRectangle, lbl.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            pnlStatusLabels.Controls.Add(lbl);
        }

        private void HighlightSqlKeywords(RichTextBox rtb)
        {
            if (string.IsNullOrEmpty(rtb.Text)) return;

            string[] keywords = { 
                "CREATE", "TABLE", "VIEW", "FUNCTION", "TRIGGER", "INDEX", "CONSTRAINT", "EXTENSION", "ROLE", "SEQUENCE", "ENUM", "MATERIALIZED VIEW", 
                "ALTER", "DROP", "ADD", "COLUMN", "INSERT", "INTO", "UPDATE", "DELETE", "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "NULL", 
                "PRIMARY KEY", "FOREIGN KEY", "REFERENCES", "UNIQUE", "CHECK", "DEFAULT", "WITH", "WITHOUT", "TIME", "ZONE", "AS", "CONSTRAINT", "IF", "EXISTS"
            };
            
            string[] types = {
                "int", "integer", "bigint", "smallint", "numeric", "decimal", "boolean", "varchar", "char", "character", "varying", "text", "uuid", "timestamp", "date", "json", "jsonb", "double", "precision", "real"
            };

            int originalSelectionStart = rtb.SelectionStart;
            int originalSelectionLength = rtb.SelectionLength;
            Color originalColor = UIConstants.TextPrimary;

            SendMessage(rtb.Handle, WM_SETREDRAW, 0, 0);
            try {
                rtb.SelectAll();
                rtb.SelectionColor = originalColor;

                // Keywords
                foreach (string kw in keywords) HighlightWord(rtb, kw, UIConstants.SqlKeyword, true);
                
                // Types
                foreach (string type in types) HighlightWord(rtb, type, UIConstants.SqlType, false);
                
                // Strings
                int strIdx = 0;
                while ((strIdx = rtb.Text.IndexOf('\'', strIdx)) != -1) {
                    int endStr = rtb.Text.IndexOf('\'', strIdx + 1);
                    if (endStr == -1) break;
                    rtb.Select(strIdx, endStr - strIdx + 1);
                    rtb.SelectionColor = UIConstants.SqlString;
                    strIdx = endStr + 1;
                }

                // Comments
                int commentIdx = 0;
                while ((commentIdx = rtb.Text.IndexOf("--", commentIdx)) != -1) {
                    int endLine = rtb.Text.IndexOf('\n', commentIdx);
                    if (endLine == -1) endLine = rtb.Text.Length;
                    rtb.Select(commentIdx, endLine - commentIdx);
                    rtb.SelectionColor = UIConstants.SqlComment;
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Italic);
                    commentIdx = endLine;
                }
            } finally {
                rtb.Select(originalSelectionStart, originalSelectionLength);
                SendMessage(rtb.Handle, WM_SETREDRAW, 1, 0);
                rtb.Invalidate();
            }
        }

        private void HighlightWord(RichTextBox rtb, string word, Color color, bool bold)
        {
            int index = 0;
            while ((index = rtb.Text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) != -1) {
                bool isStart = index == 0 || !char.IsLetterOrDigit(rtb.Text[index - 1]);
                bool isEnd = index + word.Length == rtb.Text.Length || !char.IsLetterOrDigit(rtb.Text[index + word.Length]);
                if (isStart && isEnd) {
                    rtb.Select(index, word.Length);
                    rtb.SelectionColor = color;
                    if (bold) rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                }
                index += word.Length;
            }
        }

        // Win32 API for scroll synchronization
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_VSCROLL = 0x0115;
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;

        private void SyncGutterScroll(RichTextBox source, RichTextBox gutter)
        {
            // Get the first visible line index from the source
            int charIndex = source.GetCharIndexFromPosition(new Point(0, 0));
            int lineIndex = source.GetLineFromCharIndex(charIndex);

            // Get the character index of the same line in the gutter
            int gutterCharIndex = gutter.GetFirstCharIndexFromLine(lineIndex);
            if (gutterCharIndex < 0) return;

            // Scroll gutter to that position
            gutter.Select(gutterCharIndex, 0);
            gutter.ScrollToCaret();
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
        private List<SchemaDiffResult> GetCheckedSchemaDiffs(TreeNodeCollection nodes)
        {
            var results = new List<SchemaDiffResult>();
            foreach (TreeNode node in nodes)
            {
                if (node.Checked && node.Tag is SchemaDiffResult diff)
                {
                    results.Add(diff);
                }
                if (node.Nodes.Count > 0)
                {
                    results.AddRange(GetCheckedSchemaDiffs(node.Nodes));
                }
            }
            return results;
        }

        private void OpenSqlEditor(string filePath, string title)
        {
            if (!File.Exists(filePath)) return;
            string sql = File.ReadAllText(filePath);
            using (var editor = new SqlScriptEditorForm(sql, title, "Save Changes"))
            {
                if (editor.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(filePath, editor.EditedScript);
                    AddStatusBadge("Script updated & saved", UIConstants.Success);
                }
            }
        }
    }
}
