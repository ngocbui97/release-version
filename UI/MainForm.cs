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
        private ComboBox cmbAiProvider = null!, cmbAiModel = null!;
        private Label lblOldDbStatus = null!, lblNewDbStatus = null!;
        private Label lblSourceSchemaHeader = null!, lblTargetSchemaHeader = null!;
        private GroupBox gbSourceData = null!, gbTargetData = null!;
        private DatabaseConfig _oldDbConfig = null!, _newDbConfig = null!;
        private Button btnConnect = null!;
        
        // Tab 2: Databases Backup
        private Button btnBackupOld = null!;
        private ComboBox cmbRestoreConnection = null!;
        private ListBox lstPostRestoreSqls = null!;
        private List<string> _postRestoreSqlFiles = new List<string>();
        private readonly Dictionary<string, string> _sqlFileStatuses = new Dictionary<string, string>();
        private CheckBox chkCleanBefore = null!, chkSingleTransaction = null!, chkOnlySchema = null!, chkOnlyData = null!;
        private CheckBox chkNoOwner = null!, chkNoPrivileges = null!, chkDisableTriggers = null!, chkNoTablespaces = null!;
        private CheckBox chkVerboseRestore = null!;
        private NumericUpDown numRestoreJobs = null!;
        private TextBox txtRoleName = null!;
        private ComboBox cmbRestoreFormat = null!, cmbRestoreSection = null!;
        private CheckBox chkIncludeCreateDb = null!, chkNoDataFailedTables = null!, chkExitOnError = null!, chkUseSetSessionAuth = null!;
        private FlowLayoutPanel pnlExtensions = null!;
        private Panel txtRestoreFilePath = null!;

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
        private CheckBox chkDryRun = null!;
        private TextBox txtExecuteLog = null!, txtFinalExportLog = null!, txtBackupLog = null!, txtConfigDiffLog = null!, txtAiReviewLog = null!;
        private readonly List<string> _restoreLogLines = new List<string>();
        private TextBox txtLogFilter = null!;
        private ComboBox cmbLogFilterType = null!;
        private FlowLayoutPanel pnlStatusLabels = null!, pnlDataStatusLabels = null!, pnlTreeToolbar = null!;
        private RichTextBox txtSourceDdl = null!, txtTargetDdl = null!, txtSourceLineNumbers = null!, txtTargetLineNumbers = null!;
        private TextBox txtIgnoreColumns = null!, txtDataFilter = null!;
        private CheckBox chkUseUpsert = null!;
        private Label lblDataStatus = null!, lblAiReviewStatus = null!;
        private Label lblAiKeyReadiness = null!, lblAiSchemaReadiness = null!, lblAiDataReadiness = null!, lblAiConfigReadiness = null!;
        private List<SchemaDiffResult> _schemaDiffs = new List<SchemaDiffResult>();
        private Label lblSourceDdlHeader = null!, lblTargetDdlHeader = null!;
        private Label lblSourceDataDbTitle = null!, lblTargetDataDbTitle = null!;
        private ProgressBar pbDataLoading = null!;
        private Button btnRefreshTables = null!;
        
        // Tab 6: Compare Config
        private TextBox txtOldConfigPath = null!, txtNewConfigPath = null!;
        private Button btnSelectOldConfig = null!, btnSelectNewConfig = null!, btnCompareConfig = null!;    
        private CheckBox chkTuningSchema = null!, chkTuningData = null!, chkIncludeOwner = null!, chkIgnoreExtension = null!;
        private RadioButton rbCompareFile = null!, rbCompareFolder = null!;

        // Tab 7: Final Export + Tab 8: AI Review
        private Button btnReviewSchema = null!, btnReviewConfig = null!, btnReviewData = null!, btnGenerateSchema = null!;
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
        private DatabaseConfig? _customJunkConfig, _customRestoreConfig, _customFinalExportConfig;
        private PostgresService? _customJunkPgService, _customRestorePgService, _customFinalExportPgService;
        private ComboBox cmbFinalExportConnection = null!;
        private CheckedListBox clbFinalExportDbs = null!;
        private string? _lastSchemaExportPath, _lastDataExportPath;
        private bool _isUpdatingRestoreConnection = false;
        private System.Threading.CancellationTokenSource? _restoreCts;

        private static readonly System.Text.RegularExpressions.Regex PsqlNoiseRegex = new System.Text.RegularExpressions.Regex(
            @"^(BEGIN|COMMIT|ROLLBACK|SET|GRANT|REVOKE|ANALYZE|VACUUM|INSERT \d+ \d+|UPDATE \d+|DELETE \d+|SELECT \d+|COPY \d+|COMMENT|DO|(CREATE|ALTER|DROP) [A-Z ]+)$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex PsqlPathCleanupRegex = new System.Text.RegularExpressions.Regex(
            @"^psql:.+?:(\d+):\s*(NOTICE|ERROR|WARNING|INFO|CONTEXT):\s*(.*)$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex PsqlFallbackCleanupRegex = new System.Text.RegularExpressions.Regex(
            @"^psql:.+?:(\d+):\s*(.*)$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Tab 10: Other (Script Converter)
        private TextBox txtConvertSourceFile = null!;
        private Button btnBrowseConvertFile = null!;
        private Button btnAnalyzeScript = null!;
        private Button btnReviewScript = null!;
        private Button btnConvertScript = null!;
        private TextBox txtConvertLog = null!;
        private CheckBox chkConvertTuning = null!;
        private CheckBox chkIgnoreOwner = null!;
        private CheckBox chkIgnorePrivileges = null!;
        private CheckBox chkIgnoreTablespaces = null!;
        private CheckBox chkIgnoreComments = null!;
        private CheckBox chkIgnorePublications = null!;
        private CheckBox chkIgnoreSubscriptions = null!;
        private CheckBox chkIgnoreSecurityLabels = null!;
        private CheckBox chkIgnoreTableAccessMethods = null!;
        private CheckBox chkIgnoreData = null!;
        private CheckBox chkIgnoreSchema = null!;
        private CheckBox chkIgnoreTransaction = null!;
        private Button btnOpenConvertFolder = null!;
        private string? _lastConvertedScriptPath;

        // Services
        private PostgresService? _oldPgService;
        private PostgresService? _newPgService;
        private DatabaseCompareService? _dbCompareService;
        private FileSystemService? _fileSystemService;
        private AIOperationService? _aiService;
        private bool _suppressComboEvents = false; 
        private bool _suppressConfigEvents = false; 
        private bool _isInitializingJunk = false; // Guard for startup Junk Tab population
        private bool _isAiReviewRunning = false;
        private string _configCompareSourceFile = "";
        private string _configCompareTargetFile = "";
        private string _configCompareSourceFolder = "";
        private string _configCompareTargetFolder = "";

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
            UpdateRestoreConnectionItems();
        }

        private void UpdateRestoreConnectionItems()
        {
            if (cmbRestoreConnection == null) return;

            _isUpdatingRestoreConnection = true;
            try
            {
                var selectedIndex = cmbRestoreConnection.SelectedIndex;
                cmbRestoreConnection.BeginUpdate();
                cmbRestoreConnection.Items.Clear();

                string sourceText = "Source (Dev)";
                if (_newDbConfig != null)
                {
                    sourceText += $" ({_newDbConfig.Host}:{_newDbConfig.Port})";
                }

                string targetText = "Target (Prod)";
                if (_oldDbConfig != null)
                {
                    targetText += $" ({_oldDbConfig.Host}:{_oldDbConfig.Port})";
                }

                string customText = "Custom Connection...";
                if (_customRestoreConfig != null)
                {
                    customText += $" ({_customRestoreConfig.Host}:{_customRestoreConfig.Port})";
                }

                cmbRestoreConnection.Items.AddRange(new string[] { sourceText, targetText, customText });

                if (selectedIndex >= 0 && selectedIndex < cmbRestoreConnection.Items.Count)
                {
                    cmbRestoreConnection.SelectedIndex = selectedIndex;
                }
                else
                {
                    cmbRestoreConnection.SelectedIndex = 0;
                }
                cmbRestoreConnection.EndUpdate();
            }
            finally
            {
                _isUpdatingRestoreConnection = false;
            }
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
                var lblTitle = new Label { Text = title, Dock = DockStyle.Top, Height = 26, Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.Primary, Padding = new Padding(0, 4, 0, 0) };
                pnl.Controls.Add(lblTitle);
            }
            return pnl;
        }

        private void UpdateStatusBadge(Label lbl, bool? isValid, string info)
        {
            lbl.AutoSize = true;
            lbl.Padding = new Padding(10, 8, 10, 8);
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

        private ComboBox AddSettingComboRow(TableLayoutPanel grid, string label, string[] items, int selectedIndex = 0, bool isEditable = false)
        {
            var rowIndex = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary };
            var cmb = new ComboBox { 
                Dock = DockStyle.Top, 
                Margin = new Padding(10, 8, 10, 0), 
                DropDownStyle = isEditable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, 
                Font = new Font(UIConstants.MainFontName, 9.5f) 
            };
            cmb.Items.AddRange(items);
            if (items.Length > 0) cmb.SelectedIndex = selectedIndex;
            
            grid.Controls.Add(lbl, 0, rowIndex);
            grid.Controls.Add(cmb, 1, rowIndex);
            
            return cmb;
        }

        private async Task RefreshModelQuotaStatusAsync()
        {
            if (cmbAiProvider == null || cmbAiModel == null || txtAiKey == null) return;
            var provider = cmbAiProvider.SelectedItem?.ToString() ?? "Gemini";
            var apiKey = txtAiKey.Text;

            string[] models;
            if (provider == "Gemini")
                models = new string[] { "gemini-3.5-flash", "gemini-3.5-pro", "gemini-3.1-flash-lite", "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.5-flash-lite", "gemini-2.0-flash", "gemini-2.0-flash-thinking-exp", "gemini-1.5-flash", "gemini-1.5-pro", "gemini-1.5-flash-8b" };
            else if (provider == "OpenAI")
                models = new string[] { "gpt-4o", "gpt-4o-mini", "gpt-4" };
            else if (provider == "Claude")
                models = new string[] { "claude-3-5-sonnet-latest", "claude-3-5-haiku-latest", "claude-3-opus-latest" };
            else if (provider == "Github Copilot")
                models = new string[] { "gpt-4o", "claude-3.5-sonnet" };
            else if (provider == "OpenRouter")
                models = new string[] { 
                    "google/gemini-2.5-flash", 
                    "google/gemini-2.5-pro", 
                    "google/gemini-2.0-flash-thinking-exp:free",
                    "anthropic/claude-3.5-sonnet", 
                    "anthropic/claude-3.5-haiku",
                    "openai/gpt-4o", 
                    "openai/gpt-4o-mini",
                    "deepseek/deepseek-chat", 
                    "deepseek/deepseek-coder",
                    "meta-llama/llama-3.1-70b-instruct",
                    "meta-llama/llama-3.1-405b-instruct",
                    "qwen/qwen-2.5-coder-32b-instruct"
                };
            else
                models = new string[] { };

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                cmbAiModel.BeginUpdate();
                cmbAiModel.Items.Clear();
                cmbAiModel.Items.AddRange(models);
                if (cmbAiModel.Items.Count > 0) cmbAiModel.SelectedIndex = 0;
                cmbAiModel.EndUpdate();
                return;
            }

            var currentSelectedModel = string.IsNullOrWhiteSpace(cmbAiModel.Text) ? "" : cmbAiModel.Text.Split(' ')[0];
            
            cmbAiModel.BeginUpdate();
            cmbAiModel.Items.Clear();
            foreach (var m in models)
            {
                cmbAiModel.Items.Add($"{m} (Checking...)");
            }
            int selectIdx = -1;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == currentSelectedModel) selectIdx = i;
            }
            if (selectIdx >= 0) cmbAiModel.SelectedIndex = selectIdx;
            else if (cmbAiModel.Items.Count > 0) cmbAiModel.SelectedIndex = 0;
            cmbAiModel.EndUpdate();

            // Run checks sequentially in a single background thread with a delay to avoid rate limiting / concurrency limits
            _ = Task.Run(async () => {
                for (int i = 0; i < models.Length; i++)
                {
                    var model = models[i];
                    var index = i;
                    var status = await AIOperationService.GetModelStatusAsync(provider, apiKey, model);
                    if (this.IsDisposed) return;
                    this.BeginInvoke(new Action(() => {
                        if (this.IsDisposed) return;
                        if (cmbAiProvider.SelectedItem?.ToString() == provider && txtAiKey.Text == apiKey && cmbAiModel.Items.Count > index)
                        {
                            var isSelected = (cmbAiModel.SelectedIndex == index);
                            cmbAiModel.Items[index] = $"{model} ({status})";
                            if (isSelected)
                            {
                                cmbAiModel.SelectedIndex = index;
                            }
                        }
                    }));
                    await Task.Delay(250); // 250ms delay between checks to avoid free-tier concurrency limits
                }
            });
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
            // Height=96px: Padding.Top=18 + Title=26 + Button=34 + bottom spacing = 96
            var cardSource = CreateCardPanel("SOURCE CONNECTION (New/Dev Development)", 750, 96);
            // Button at y=46: just below title (18+26=44) with 2px gap. Height=34 for better clickability.
            var btnConfigNewDb = new Button { Text = "⊙  Select Source Connection...", Width = 230, Height = 34, Location = new Point(15, 46) };
            StyleButtonSecondary(btnConfigNewDb);
            btnConfigNewDb.Font = new Font(UIConstants.MainFontName, 9f);
            
            var btnTestNewDb = new Button { Text = "⚡  Test Connection", Width = 140, Height = 34, Location = new Point(255, 46) };
            StyleButtonSecondary(btnTestNewDb);
            btnTestNewDb.Font = new Font(UIConstants.MainFontName, 9f);
            
            // Badge at y=46: vertically aligned with buttons (Y=46) and uses taller padding
            lblNewDbStatus = new Label { Text = "Not Configured", Location = new Point(405, 46), AutoSize = true, Padding = new Padding(10, 8, 10, 8) };
            UpdateStatusBadge(lblNewDbStatus, null, "");
            btnConfigNewDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Source Database Connection", _newDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _newDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            btnTestNewDb.Click += async (s, e) => {
                if (_newDbConfig == null) {
                    MessageBox.Show("Please select a Source connection first.", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                btnTestNewDb.Text = "⌛ Testing...";
                btnTestNewDb.Enabled = false;
                try {
                    await using (var conn = new Npgsql.NpgsqlConnection(_newDbConfig.GetConnectionString())) {
                        await conn.OpenAsync();
                    }
                    MessageBox.Show("Connection to Source Database succeeded!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) {
                    MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally {
                    btnTestNewDb.Text = "⚡  Test Connection";
                    btnTestNewDb.Enabled = true;
                }
            };
            cardSource.Controls.Add(btnConfigNewDb); cardSource.Controls.Add(btnTestNewDb); cardSource.Controls.Add(lblNewDbStatus);
            panelConfig.Controls.Add(cardSource);

            // --- TARGET CONNECTION CARD --- (same dimensions as source)
            var cardTarget = CreateCardPanel("TARGET CONNECTION (Old/Prod Maintenance)", 750, 96);
            cardTarget.Margin = new Padding(0, 8, 0, 0);
            var btnConfigOldDb = new Button { Text = "⊙  Select Target Connection...", Width = 230, Height = 34, Location = new Point(15, 46) };
            StyleButtonSecondary(btnConfigOldDb);
            btnConfigOldDb.Font = new Font(UIConstants.MainFontName, 9f);
            
            var btnTestOldDb = new Button { Text = "⚡  Test Connection", Width = 140, Height = 34, Location = new Point(255, 46) };
            StyleButtonSecondary(btnTestOldDb);
            btnTestOldDb.Font = new Font(UIConstants.MainFontName, 9f);
            
            lblOldDbStatus = new Label { Text = "Not Configured", Location = new Point(405, 46), AutoSize = true, Padding = new Padding(10, 8, 10, 8) };
            UpdateStatusBadge(lblOldDbStatus, null, "");
            btnConfigOldDb.Click += (s, e) => { using (var dlg = new ConnectionDialog("Target Database Connection", _oldDbConfig)) { if (dlg.ShowDialog() == DialogResult.OK) { _oldDbConfig = dlg.Config; UpdateConnectionLabels(); } } };
            btnTestOldDb.Click += async (s, e) => {
                if (_oldDbConfig == null) {
                    MessageBox.Show("Please select a Target connection first.", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                btnTestOldDb.Text = "⌛ Testing...";
                btnTestOldDb.Enabled = false;
                try {
                    await using (var conn = new Npgsql.NpgsqlConnection(_oldDbConfig.GetConnectionString())) {
                        await conn.OpenAsync();
                    }
                    MessageBox.Show("Connection to Target Database succeeded!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) {
                    MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally {
                    btnTestOldDb.Text = "⚡  Test Connection";
                    btnTestOldDb.Enabled = true;
                }
            };
            cardTarget.Controls.Add(btnConfigOldDb); cardTarget.Controls.Add(btnTestOldDb); cardTarget.Controls.Add(lblOldDbStatus);
            panelConfig.Controls.Add(cardTarget);

            // --- GENERAL SETTINGS CARD ---
            // Height: 46(offset) + 7×42(rows) + 15(padding bottom) = 355px
            var cardSettings = CreateCardPanel("GENERAL PROJECT SETTINGS", 750, 355);
            cardSettings.Margin = new Padding(0, 8, 0, 0);

            var pnlSettingsGrid = new TableLayoutPanel {
                Location = new Point(0, 46),   // Padding.Top(18) + title(26) + 2px gap
                Width = 720,
                Height = 294,                   // exactly 7 rows × 42px
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
            
            cmbAiProvider    = AddSettingComboRow(pnlSettingsGrid, "AI Provider:", new string[] { "Gemini", "OpenAI", "Claude", "Github Copilot", "OpenRouter" });
            cmbAiModel       = AddSettingComboRow(pnlSettingsGrid, "AI Model:", new string[] { "gemini-3.5-flash", "gemini-3.5-pro", "gemini-3.1-flash-lite", "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.5-flash-lite", "gemini-2.0-flash", "gemini-2.0-flash-thinking-exp", "gemini-1.5-flash", "gemini-1.5-pro", "gemini-1.5-flash-8b" }, 0, true);
            txtAiKey         = AddSettingRow(pnlSettingsGrid, "AI API Key (optional):", "", false, true);

            cmbAiProvider.SelectedIndexChanged += async (s, e) => {
                await RefreshModelQuotaStatusAsync();
                UpdateAiReadinessStatus();
            };

            System.Windows.Forms.Timer debounceTimer = new System.Windows.Forms.Timer();
            debounceTimer.Interval = 1000;
            debounceTimer.Tick += async (s, e) => {
                debounceTimer.Stop();
                await RefreshModelQuotaStatusAsync();
            };
            txtAiKey.TextChanged += (s, e) => {
                debounceTimer.Stop();
                debounceTimer.Start();
                UpdateAiReadinessStatus();
            };

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

            // TableLayoutPanel: Col 0 = stretch filler | Col 1 = Save Settings | Col 2 = Open Output | Col 3 = Initialize
            // This guarantees Initialize Session is ALWAYS on the far right.
            var tblFooter = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // filler
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // Save Settings
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // Open Output
            tblFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260)); // Initialize Session
            tblFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var btnSaveSettings = new Button { Text = "💾  Save Settings", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            StyleButtonSecondary(btnSaveSettings); btnSaveSettings.Font = new Font(UIConstants.MainFontName, 9f);
            btnSaveSettings.Click += (s, e) => {
                SaveConfig();
                MessageBox.Show("General project settings saved successfully!", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

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
            tblFooter.Controls.Add(btnSaveSettings, 1, 0);
            tblFooter.Controls.Add(btnOpenReleaseFolder, 2, 0);
            tblFooter.Controls.Add(btnConnect, 3, 0);
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
            var cardRestore = CreateCardPanel("RESTORE FROM BACKUP FILE", 800, 310);
            
            var pnlRestoreGrid = new TableLayoutPanel {
                Location = new Point(15, 40),
                Width = 750,
                Height = 235,
                ColumnCount = 3,
                RowCount = 5
            };
            pnlRestoreGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            pnlRestoreGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlRestoreGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            pnlRestoreGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            pnlRestoreGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            pnlRestoreGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            pnlRestoreGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            pnlRestoreGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            var lblConnLabel = new Label { Text = "Connection:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            cmbRestoreConnection = new ComboBox { Name = "cmbRestoreConnection", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 0, 2), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 9f) };
            UpdateRestoreConnectionItems();

            cmbRestoreConnection.SelectedIndexChanged += async (s, e) => {
                if (_isUpdatingRestoreConnection) return;
                if (cmbRestoreConnection.SelectedIndex == 2) // Custom
                {
                    using (var dlg = new ConnectionDialog("Custom Restore Target", _customRestoreConfig))
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _customRestoreConfig = dlg.Config;
                            _customRestorePgService = new PostgresService(_customRestoreConfig) { PostgresBinPath = txtPgBinPath.Text };
                            UpdateRestoreConnectionItems();
                        }
                    }
                }
                await RefreshRequiredExtensionsAsync();
            };

            var lblTargetDb = new Label { Text = "Database:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            var txtTargetDbName = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 0, 2), PlaceholderText = "e.g. my_prod_restore_v1", Font = new Font(UIConstants.MainFontName, 9f), BorderStyle = BorderStyle.FixedSingle };

            var lblRestoreFile = new Label { Text = "Backup File:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            txtRestoreFilePath = new Panel { 
                Name = "txtRestoreFilePath", 
                Anchor = AnchorStyles.Left | AnchorStyles.Right, 
                Margin = new Padding(0, 7, 0, 7), 
                Font = new Font(UIConstants.MainFontName, 9f), 
                Text = "", 
                ForeColor = UIConstants.TextSecondary,
                BackColor = Color.White,
                Height = 24,
                Visible = false
            };
            txtRestoreFilePath.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, txtRestoreFilePath.Width - 1, txtRestoreFilePath.Height - 1);
                var radius = 4;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                    path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                    path.AddArc(rect.Right - (radius * 2), rect.Y, radius * 2, radius * 2, 270, 90);
                    path.AddArc(rect.Right - (radius * 2), rect.Bottom - (radius * 2), radius * 2, radius * 2, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - (radius * 2), radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();
                    using (var br = new SolidBrush(txtRestoreFilePath.BackColor)) {
                        g.FillPath(br, path);
                    }
                    var hasFile = txtRestoreFilePath.Tag != null;
                    var borderColor = hasFile ? UIConstants.Primary : UIConstants.Border;
                    using (var pen = new Pen(borderColor, 1)) {
                        g.DrawPath(pen, path);
                    }
                }
                var font = txtRestoreFilePath.Font;
                var textColor = txtRestoreFilePath.ForeColor;
                var textRect = new Rectangle(8, 0, txtRestoreFilePath.Width - 16, txtRestoreFilePath.Height);
                TextRenderer.DrawText(g, txtRestoreFilePath.Text, font, textRect, textColor, 
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            txtRestoreFilePath.MouseMove += (s, e) => {
                if (txtRestoreFilePath.Tag is string fullPath) {
                    var currentTip = tooltip.GetToolTip(txtRestoreFilePath);
                    if (currentTip != fullPath) {
                        tooltip.SetToolTip(txtRestoreFilePath, fullPath);
                    }
                } else {
                    tooltip.SetToolTip(txtRestoreFilePath, "");
                }
            };
            var btnBrowseRestoreFile = new Button { Text = "Browse...", Width = 80, Height = 26, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0), Font = new Font(UIConstants.MainFontName, 9f) };
            StyleButtonSecondary(btnBrowseRestoreFile);

            btnBrowseRestoreFile.Click += async (s, e) => {
                using (var ofd = new OpenFileDialog { Filter = "Backup Files (*.backup)|*.backup|SQL Scripts (*.sql)|*.sql" }) {
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtRestoreFilePath.Text = Path.GetFileName(ofd.FileName);
                        txtRestoreFilePath.Tag = ofd.FileName;
                        txtRestoreFilePath.BackColor = Color.FromArgb(232, 242, 252);
                        txtRestoreFilePath.ForeColor = UIConstants.Primary;
                        txtRestoreFilePath.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
                        txtRestoreFilePath.Visible = true;
                        txtRestoreFilePath.Invalidate();
                        await RefreshRequiredExtensionsAsync();
                    }
                }
            };

            var lblExtensions = new Label { 
                Text = "Extensions:", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.TopRight, 
                Padding = new Padding(0, 7, 0, 0),
                Font = new Font(UIConstants.MainFontName, 9f), 
                ForeColor = UIConstants.TextSecondary 
            };
            
            pnlExtensions = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(0, 0, 18, 0) // Reserve space for vertical scrollbar
            };
            
            // Initialize with placeholder
            var lblSelectPlaceholder = new Label {
                Text = "(Select a file to scan extensions)",
                Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Italic),
                ForeColor = Color.Gray,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 3, 0, 3)
            };
            pnlExtensions.Controls.Add(lblSelectPlaceholder);

            btnBackupOld = new Button { Text = "↻  Restore DB from File...", Width = 220, Height = 34, Anchor = AnchorStyles.Right, Margin = new Padding(0, 6, 0, 0) };
            StyleButtonPrimary(btnBackupOld); btnBackupOld.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold);

            btnBackupOld.Click += (object? s, EventArgs e) => {
                if (_restoreCts != null) {
                    btnBackupOld.Enabled = false;
                    btnBackupOld.Text = "⌛ Cancelling...";
                    _restoreCts.Cancel();
                    return;
                }

                var fullPath = txtRestoreFilePath.Tag as string;
                if (string.IsNullOrWhiteSpace(fullPath)) {
                    MessageBox.Show("Please select a backup or SQL file to restore first.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var selectedService = cmbRestoreConnection.SelectedIndex == 2 ? _customRestorePgService : (cmbRestoreConnection.SelectedIndex == 0 ? _newPgService : _oldPgService);
                var defaultDbName = cmbRestoreConnection.SelectedIndex == 2 ? (_customRestoreConfig?.DatabaseName ?? "") : (cmbRestoreConnection.SelectedIndex == 0 ? NewDbName : OldDbName);
                
                if (cmbRestoreConnection.SelectedIndex == 2 && selectedService == null) {
                    MessageBox.Show("Please select a valid custom connection first.", "Custom Connection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _restoreCts = new System.Threading.CancellationTokenSource();
                btnBackupOld.Text = "🛑 Stop Restore";
                btnBackupOld.BackColor = UIConstants.Danger;

                RestoreDbAsync(selectedService, defaultDbName, txtTargetDbName.Text.Trim(), fullPath);
            };

            pnlRestoreGrid.Controls.Add(lblConnLabel, 0, 0);
            pnlRestoreGrid.Controls.Add(cmbRestoreConnection, 1, 0);
            pnlRestoreGrid.SetColumnSpan(cmbRestoreConnection, 2);
            
            pnlRestoreGrid.Controls.Add(lblTargetDb, 0, 1);
            pnlRestoreGrid.Controls.Add(txtTargetDbName, 1, 1);
            pnlRestoreGrid.SetColumnSpan(txtTargetDbName, 2);
            
            pnlRestoreGrid.Controls.Add(lblRestoreFile, 0, 2);
            pnlRestoreGrid.Controls.Add(txtRestoreFilePath, 1, 2);
            pnlRestoreGrid.Controls.Add(btnBrowseRestoreFile, 2, 2);

            pnlRestoreGrid.Controls.Add(lblExtensions, 0, 3);
            pnlRestoreGrid.Controls.Add(pnlExtensions, 1, 3);
            pnlRestoreGrid.SetColumnSpan(pnlExtensions, 2);
            
            pnlRestoreGrid.Controls.Add(btnBackupOld, 1, 4);
            pnlRestoreGrid.SetColumnSpan(btnBackupOld, 2);

            cardRestore.Controls.Add(pnlRestoreGrid);

            // --- CARD: Restore Options ---
            var cardRestoreOptions = CreateCardPanel("RESTORE OPTIONS", 800, 310);
            
            var pnlRestoreOptionsGrid = new TableLayoutPanel {
                Location = new Point(15, 40),
                Width = 750,
                Height = 255,
                ColumnCount = 2,
                RowCount = 8
            };
            pnlRestoreOptionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlRestoreOptionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            pnlRestoreOptionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            chkCleanBefore = new CheckBox { Text = "Clean Before", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkSingleTransaction = new CheckBox { Text = "Single Trans", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkIncludeCreateDb = new CheckBox { Text = "Create DB", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            
            chkOnlySchema = new CheckBox { Text = "Only Schema", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkOnlyData = new CheckBox { Text = "Only Data", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkDisableTriggers = new CheckBox { Text = "Disable Triggers", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            
            chkNoOwner = new CheckBox { Text = "No Owner", Checked = true, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkNoPrivileges = new CheckBox { Text = "No Privilege", Checked = true, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkNoTablespaces = new CheckBox { Text = "No Tablespace", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            
            chkNoDataFailedTables = new CheckBox { Text = "No Data Fail Tbl", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkExitOnError = new CheckBox { Text = "Exit On Error", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            chkUseSetSessionAuth = new CheckBox { Text = "SET SESSION AUTH", Checked = false, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };
            
            chkVerboseRestore = new CheckBox { Text = "Verbose Log", Checked = true, Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextPrimary, AutoSize = true };

            var pnlJobs = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var lblJobs = new Label { Text = "Jobs:", Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextSecondary, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Margin = new Padding(0, 4, 4, 0) };
            numRestoreJobs = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 64, Font = new Font(UIConstants.MainFontName, 8f), Width = 45, Height = 20 };
            pnlJobs.Controls.Add(lblJobs);
            pnlJobs.Controls.Add(numRestoreJobs);
            pnlJobs.Controls.Add(chkVerboseRestore);

            chkOnlySchema.CheckedChanged += (s, e) => {
                if (chkOnlySchema.Checked) chkOnlyData.Checked = false;
            };
            chkOnlyData.CheckedChanged += (s, e) => {
                if (chkOnlyData.Checked) chkOnlySchema.Checked = false;
            };

            var lblFormat = new Label { Text = "Format:", Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Right };
            cmbRestoreFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 8f), Margin = new Padding(2, 0, 4, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            cmbRestoreFormat.Items.AddRange(new string[] { "Auto", "Custom", "Tar", "Directory", "Plain" });
            cmbRestoreFormat.SelectedIndex = 0;

            var lblSection = new Label { Text = "Sec:", Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Right };
            cmbRestoreSection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 8f), Margin = new Padding(2, 0, 4, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            cmbRestoreSection.Items.AddRange(new string[] { "All", "Pre-data", "Data", "Post-data" });
            cmbRestoreSection.SelectedIndex = 0;

            var lblRole = new Label { Text = "Role:", Font = new Font(UIConstants.MainFontName, 8f), ForeColor = UIConstants.TextSecondary, AutoSize = true, Anchor = AnchorStyles.Right };
            txtRoleName = new TextBox { Font = new Font(UIConstants.MainFontName, 8f), Margin = new Padding(2, 0, 2, 0), BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            var pnlBottomRow = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0)
            };
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); // Format label
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); // Format combobox
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35)); // Sec label
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); // Sec combobox
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); // Role label
            pnlBottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f)); // Role textbox
            pnlBottomRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            pnlBottomRow.Controls.Add(lblFormat, 0, 0);
            pnlBottomRow.Controls.Add(cmbRestoreFormat, 1, 0);
            pnlBottomRow.Controls.Add(lblSection, 2, 0);
            pnlBottomRow.Controls.Add(cmbRestoreSection, 3, 0);
            pnlBottomRow.Controls.Add(lblRole, 4, 0);
            pnlBottomRow.Controls.Add(txtRoleName, 5, 0);

            pnlRestoreOptionsGrid.Controls.Add(chkCleanBefore, 0, 0);
            pnlRestoreOptionsGrid.Controls.Add(chkNoOwner, 1, 0);
            
            pnlRestoreOptionsGrid.Controls.Add(chkSingleTransaction, 0, 1);
            pnlRestoreOptionsGrid.Controls.Add(chkNoPrivileges, 1, 1);
            
            pnlRestoreOptionsGrid.Controls.Add(chkOnlySchema, 0, 2);
            pnlRestoreOptionsGrid.Controls.Add(chkNoTablespaces, 1, 2);
            
            pnlRestoreOptionsGrid.Controls.Add(chkOnlyData, 0, 3);
            pnlRestoreOptionsGrid.Controls.Add(chkNoDataFailedTables, 1, 3);
            
            pnlRestoreOptionsGrid.Controls.Add(chkIncludeCreateDb, 0, 4);
            pnlRestoreOptionsGrid.Controls.Add(chkExitOnError, 1, 4);
            
            pnlRestoreOptionsGrid.Controls.Add(chkDisableTriggers, 0, 5);
            pnlRestoreOptionsGrid.Controls.Add(chkUseSetSessionAuth, 1, 5);
            
            pnlRestoreOptionsGrid.Controls.Add(pnlJobs, 0, 6);
            pnlRestoreOptionsGrid.SetColumnSpan(pnlJobs, 2);

            pnlRestoreOptionsGrid.Controls.Add(pnlBottomRow, 0, 7);
            pnlRestoreOptionsGrid.SetColumnSpan(pnlBottomRow, 2);

            cardRestoreOptions.Controls.Add(pnlRestoreOptionsGrid);



            // --- CARD: SQL Scripts to Execute After Restore ---
            var cardSqlScripts = CreateCardPanel("SQL SCRIPTS TO EXECUTE AFTER RESTORE (OPTIONAL)", 800, 310);
            cardSqlScripts.Margin = new Padding(0);

            var pnlSqlGrid = new TableLayoutPanel {
                Location = new Point(15, 40),
                Width = 750,
                Height = 255,
                ColumnCount = 2,
                RowCount = 1
            };
            pnlSqlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // ListBox
            pnlSqlGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); // Buttons
            pnlSqlGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            lstPostRestoreSqls = new ListBox {
                Dock = DockStyle.Fill,
                Font = new Font(UIConstants.MainFontName, 9.5f),
                BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24
            };
            lstPostRestoreSqls.DrawItem += (s, e) => {
                if (e.Index < 0) return;
                e.DrawBackground();
                bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                string fullText = lstPostRestoreSqls.Items[e.Index].ToString() ?? "";
                int dotIdx = fullText.IndexOf(". ");
                string numPart = "";
                string namePart = fullText;
                if (dotIdx > 0) {
                    numPart = fullText.Substring(0, dotIdx + 2);
                    namePart = fullText.Substring(dotIdx + 2);
                }

                // Determine status icon
                string icon = "⚪";
                Color iconColor = Color.Gray;
                if (e.Index >= 0 && e.Index < _postRestoreSqlFiles.Count) {
                    string filePath = _postRestoreSqlFiles[e.Index];
                    if (_sqlFileStatuses.TryGetValue(filePath, out var status)) {
                        switch (status.ToLower()) {
                            case "running":
                                icon = "⚡";
                                iconColor = Color.FromArgb(255, 193, 7); // Warning/Yellow
                                break;
                            case "success":
                                icon = "✅";
                                iconColor = Color.FromArgb(40, 167, 69); // Success/Green
                                break;
                            case "error":
                                icon = "❌";
                                iconColor = Color.FromArgb(220, 53, 69); // Danger/Red
                                break;
                            default:
                                icon = "⚪";
                                iconColor = Color.Gray;
                                break;
                        }
                    }
                }

                var font = e.Font ?? lstPostRestoreSqls.Font;
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center }) {
                    Brush numBrush;
                    Brush nameBrush;
                    Brush iconBrush = new SolidBrush(iconColor);
                    if (isSelected) {
                        numBrush = SystemBrushes.HighlightText;
                        nameBrush = SystemBrushes.HighlightText;
                    } else {
                        numBrush = new SolidBrush(UIConstants.TextSecondary);
                        nameBrush = new SolidBrush(UIConstants.Primary);
                    }
                    
                    float x = e.Bounds.Left + 6;
                    
                    // Draw status icon
                    e.Graphics.DrawString(icon, font, iconBrush, new RectangleF(x, e.Bounds.Top, 20, e.Bounds.Height), sf);
                    x += 22;
                    
                    if (!string.IsNullOrEmpty(numPart)) {
                        e.Graphics.DrawString(numPart, font, numBrush, new RectangleF(x, e.Bounds.Top, 30, e.Bounds.Height), sf);
                        x += 25;
                    }
                    
                    e.Graphics.DrawString(namePart, font, nameBrush, new RectangleF(x, e.Bounds.Top, e.Bounds.Width - x, e.Bounds.Height), sf);
                    
                    iconBrush.Dispose();
                }
                e.DrawFocusRectangle();
            };



            var pnlSqlButtons = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(6, 0, 0, 0),
                WrapContents = false
            };

            var btnAddSqls = new Button { Text = "➕  Add SQLs...", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            StyleButtonSecondary(btnAddSqls);
            btnAddSqls.Font = new Font(UIConstants.MainFontName, 9f);

            var btnAddSqlsByPattern = new Button { Text = "📂  Add Pattern...", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            StyleButtonSecondary(btnAddSqlsByPattern);
            btnAddSqlsByPattern.Font = new Font(UIConstants.MainFontName, 9f);

            var btnRemoveSql = new Button { Text = "❌  Remove", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            StyleButtonSecondary(btnRemoveSql);
            btnRemoveSql.Font = new Font(UIConstants.MainFontName, 9f);

            var btnClearSqls = new Button { Text = "🧹  Clear All", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            StyleButtonSecondary(btnClearSqls);
            btnClearSqls.Font = new Font(UIConstants.MainFontName, 9f);

            var btnMoveUp = new Button { Text = "▲  Move Up", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 6) };
            StyleButtonSecondary(btnMoveUp);
            btnMoveUp.Font = new Font(UIConstants.MainFontName, 9f);
            btnMoveUp.Click += (s, e) => {
                int idx = lstPostRestoreSqls.SelectedIndex;
                if (idx > 0) {
                    var temp = _postRestoreSqlFiles[idx];
                    _postRestoreSqlFiles[idx] = _postRestoreSqlFiles[idx - 1];
                    _postRestoreSqlFiles[idx - 1] = temp;
                    
                    lstPostRestoreSqls.Items.Clear();
                    for (int i = 0; i < _postRestoreSqlFiles.Count; i++) {
                        lstPostRestoreSqls.Items.Add($"{i + 1}. {Path.GetFileName(_postRestoreSqlFiles[i])}");
                    }
                    lstPostRestoreSqls.SelectedIndex = idx - 1;
                }
            };

            var btnMoveDown = new Button { Text = "▼  Move Down", Width = 102, Height = 26, Margin = new Padding(0, 0, 0, 0) };
            StyleButtonSecondary(btnMoveDown);
            btnMoveDown.Font = new Font(UIConstants.MainFontName, 9f);
            btnMoveDown.Click += (s, e) => {
                int idx = lstPostRestoreSqls.SelectedIndex;
                if (idx >= 0 && idx < _postRestoreSqlFiles.Count - 1) {
                    var temp = _postRestoreSqlFiles[idx];
                    _postRestoreSqlFiles[idx] = _postRestoreSqlFiles[idx + 1];
                    _postRestoreSqlFiles[idx + 1] = temp;
                    
                    lstPostRestoreSqls.Items.Clear();
                    for (int i = 0; i < _postRestoreSqlFiles.Count; i++) {
                        lstPostRestoreSqls.Items.Add($"{i + 1}. {Path.GetFileName(_postRestoreSqlFiles[i])}");
                    }
                    lstPostRestoreSqls.SelectedIndex = idx + 1;
                }
            };

            pnlSqlButtons.Controls.AddRange(new Control[] { btnAddSqls, btnAddSqlsByPattern, btnRemoveSql, btnClearSqls, btnMoveUp, btnMoveDown });

            pnlSqlGrid.Controls.Add(lstPostRestoreSqls, 0, 0);
            pnlSqlGrid.Controls.Add(pnlSqlButtons, 1, 0);

            cardSqlScripts.Controls.Add(pnlSqlGrid);

            btnAddSqls.Click += (s, e) => {
                using (var ofd = new OpenFileDialog { Multiselect = true, Filter = "SQL Scripts (*.sql)|*.sql" }) {
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        foreach (var file in ofd.FileNames) {
                            if (!_postRestoreSqlFiles.Contains(file)) {
                                _postRestoreSqlFiles.Add(file);
                            }
                        }
                        _postRestoreSqlFiles = _postRestoreSqlFiles
                            .OrderBy(f => {
                                var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^\d+");
                                return match.Success && int.TryParse(match.Value, out int num) ? num : int.MaxValue;
                            })
                            .ThenBy(f => Path.GetFileName(f))
                            .ToList();
                        lstPostRestoreSqls.Items.Clear();
                        for (int i = 0; i < _postRestoreSqlFiles.Count; i++) {
                            lstPostRestoreSqls.Items.Add($"{i + 1}. {Path.GetFileName(_postRestoreSqlFiles[i])}");
                        }
                    }
                }
            };

            btnAddSqlsByPattern.Click += (s, e) => {
                using (var fbd = new FolderBrowserDialog()) {
                    if (fbd.ShowDialog() == DialogResult.OK) {
                        string defaultPattern = "";
                        if (txtTargetDbName != null && !string.IsNullOrWhiteSpace(txtTargetDbName.Text)) {
                            var parts = txtTargetDbName.Text.Split('_');
                            defaultPattern = parts[0];
                        }
                        if (string.IsNullOrEmpty(defaultPattern)) {
                            defaultPattern = "template";
                        }

                        string pattern = ShowInputDialog("Enter filename pattern (case-insensitive):", "Add SQLs by Pattern", defaultPattern);
                        if (!string.IsNullOrWhiteSpace(pattern)) {
                            var files = Directory.GetFiles(fbd.SelectedPath, "*.sql")
                                .Where(file => Path.GetFileName(file).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (files.Count == 0) {
                                MessageBox.Show($"No SQL files containing '{pattern}' found in the selected folder.", "No Matches", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }

                            int addedCount = 0;
                            foreach (var file in files) {
                                if (!_postRestoreSqlFiles.Contains(file)) {
                                    _postRestoreSqlFiles.Add(file);
                                    addedCount++;
                                }
                            }

                            if (addedCount > 0) {
                                _postRestoreSqlFiles = _postRestoreSqlFiles
                                    .OrderBy(f => {
                                        var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^\d+");
                                        return match.Success && int.TryParse(match.Value, out int num) ? num : int.MaxValue;
                                    })
                                    .ThenBy(f => Path.GetFileName(f))
                                    .ToList();

                                lstPostRestoreSqls.Items.Clear();
                                for (int i = 0; i < _postRestoreSqlFiles.Count; i++) {
                                    lstPostRestoreSqls.Items.Add($"{i + 1}. {Path.GetFileName(_postRestoreSqlFiles[i])}");
                                }
                            }
                        }
                    }
                }
            };

            btnRemoveSql.Click += (s, e) => {
                if (lstPostRestoreSqls.SelectedIndex >= 0) {
                    int idx = lstPostRestoreSqls.SelectedIndex;
                    _postRestoreSqlFiles.RemoveAt(idx);
                    
                    // Re-populate to update numbering
                    lstPostRestoreSqls.Items.Clear();
                    for (int i = 0; i < _postRestoreSqlFiles.Count; i++) {
                        lstPostRestoreSqls.Items.Add($"{i + 1}. {Path.GetFileName(_postRestoreSqlFiles[i])}");
                    }
                }
            };

            btnClearSqls.Click += (s, e) => {
                _postRestoreSqlFiles.Clear();
                lstPostRestoreSqls.Items.Clear();
            };

            // Show full path on Hover
            lstPostRestoreSqls.MouseMove += (s, e) => {
                int index = lstPostRestoreSqls.IndexFromPoint(e.Location);
                if (index >= 0 && index < _postRestoreSqlFiles.Count) {
                    string fullPath = _postRestoreSqlFiles[index];
                    string currentTip = tooltip.GetToolTip(lstPostRestoreSqls);
                    if (currentTip != fullPath) {
                        tooltip.SetToolTip(lstPostRestoreSqls, fullPath);
                    }
                } else {
                    tooltip.SetToolTip(lstPostRestoreSqls, "");
                }
            };


            // Container to lay out the three cards side-by-side
            var pnlCardsContainer = new TableLayoutPanel {
                Width = 800,
                Height = 310,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            pnlCardsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            pnlCardsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26f));
            pnlCardsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            pnlCardsContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            cardRestore.Margin = new Padding(0, 0, 8, 0);
            cardRestoreOptions.Margin = new Padding(8, 0, 8, 0);
            cardSqlScripts.Margin = new Padding(8, 0, 0, 0);

            pnlCardsContainer.Controls.Add(cardRestore, 0, 0);
            pnlCardsContainer.Controls.Add(cardRestoreOptions, 1, 0);
            pnlCardsContainer.Controls.Add(cardSqlScripts, 2, 0);

            // --- LOG HEADER CONTROL ---
            var pnlRestoreLogHeader = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = 36,
                Margin = new Padding(0, 10, 0, 4),
                ColumnCount = 6,
                RowCount = 1
            };
            pnlRestoreLogHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f)); // Title Column (no wrap)
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Spacer
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));   // "Filter:" label Column (no wrap)
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f)); // Filter TextBox
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f)); // Filter Type ComboBox
            pnlRestoreLogHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));  // Clear button (reduced width)

            var lblLogHeaderTitle = new Label {
                Text = "\uD83D\uDCDC  RESTORE LOG", AutoSize = true,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom
            };

            var lblLogFilter = new Label {
                Text = "🔍 Filter:", AutoSize = true,
                Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
            };

            txtLogFilter = new TextBox {
                Margin = new Padding(2, 0, 2, 0), Height = 22,
                Font = new Font(UIConstants.MainFontName, 8.5f), BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            txtLogFilter.TextChanged += (s, e) => ApplyLogFilter();

            cmbLogFilterType = new ComboBox {
                Margin = new Padding(2, 0, 2, 0), Height = 22,
                DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 8.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            cmbLogFilterType.Items.AddRange(new string[] { "All Logs", "Errors (❌/error)", "Success (✅)", "Info/Cmd" });
            cmbLogFilterType.SelectedIndex = 0;
            cmbLogFilterType.SelectedIndexChanged += (s, e) => ApplyLogFilter();

            var btnClearRestoreLog = new Button {
                Text = "🧹 Clear", Margin = new Padding(4, 0, 0, 0), Height = 26,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            StyleButtonSecondary(btnClearRestoreLog);
            btnClearRestoreLog.Font = new Font(UIConstants.MainFontName, 8.5f);
            btnClearRestoreLog.Click += (s, e) => {
                _restoreLogLines.Clear();
                if (txtBackupLog != null) txtBackupLog.Clear();
            };

            pnlRestoreLogHeader.Controls.Add(lblLogHeaderTitle, 0, 0);
            pnlRestoreLogHeader.Controls.Add(new Control { Visible = false }, 1, 0); // Spacer dummy
            pnlRestoreLogHeader.Controls.Add(lblLogFilter, 2, 0);
            pnlRestoreLogHeader.Controls.Add(txtLogFilter, 3, 0);
            pnlRestoreLogHeader.Controls.Add(cmbLogFilterType, 4, 0);
            pnlRestoreLogHeader.Controls.Add(btnClearRestoreLog, 5, 0);

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
            pnlBackupTop.Controls.Add(pnlCardsContainer);

            // Build tab — order matters for Dock layout (last added = topmost)
            panelBackup.Controls.Add(pnlBackupLog);      // Fill (bottom priority, added first)
            panelBackup.Controls.Add(pnlRestoreLogHeader);       // Top
            panelBackup.Controls.Add(pnlBackupTop);       // Top (topmost priority, added last)

            // Responsive width for card
            void UpdateBackupWidths() {
                var w = Math.Max(400, panelBackup.ClientSize.Width - panelBackup.Padding.Horizontal);
                pnlBackupTop.Width = w;
                pnlCardsContainer.Width = w;
                pnlRestoreGrid.Width = Math.Max(150, cardRestore.ClientSize.Width - 30);
                pnlRestoreOptionsGrid.Width = Math.Max(150, cardRestoreOptions.ClientSize.Width - 30);
                pnlSqlGrid.Width = Math.Max(150, cardSqlScripts.ClientSize.Width - 30);
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
            
            chkTuningSchema = new CheckBox { Text = "Tuning Script (Safe Run)", AutoSize = true, Location = new Point(12, 20), Checked = false, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary };
            tooltip.SetToolTip(chkTuningSchema, "Ensure output scripts contain idempotent checks (e.g. IF NOT EXISTS, DROP IF EXISTS) to prevent errors on different target databases.");

            chkIncludeOwner = new CheckBox { Text = "Include OWNER TO", AutoSize = true, Location = new Point(12, 20), Checked = false, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary };
            tooltip.SetToolTip(chkIncludeOwner, "Include ALTER ... OWNER TO statements in generated script and compare schema owner differences.");
            
            chkIgnoreExtension = new CheckBox { Text = "Ignore Extension Objects", AutoSize = true, Location = new Point(12, 20), Checked = true, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary };
            tooltip.SetToolTip(chkIgnoreExtension, "Ignore objects (functions, views, tables, indexes, triggers, etc.) belonging to PostgreSQL extensions.");

            pnlStatusLabels = new FlowLayoutPanel { 
                Location = new Point(12, 14),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent
            };
            
            pnlActionBar.Controls.AddRange(new Control[] { btnGenerateSchema, btnOpenSchemaFolder, btnEditSchema, chkTuningSchema, chkIncludeOwner, chkIgnoreExtension, pnlStatusLabels });

            void RepositionActionBar() {
                if (pnlActionBar.Width == 0) return;
                
                int startX = 12 + 160 + 8; // Start after Gen button
                if (btnOpenSchemaFolder.Visible) {
                    startX += 48;
                }
                if (btnEditSchema.Visible) {
                    startX += 48;
                }
                
                int totalCheckboxesWidth = chkTuningSchema.Width + 16 + chkIncludeOwner.Width + 16 + chkIgnoreExtension.Width + 16;
                bool twoRows = (startX + totalCheckboxesWidth + 200 > pnlActionBar.Width);
                
                if (twoRows) {
                    // Checkboxes go to row 2
                    chkTuningSchema.Location = new Point(12, 54);
                    chkIncludeOwner.Location = new Point(12 + chkTuningSchema.Width + 16, 54);
                    chkIgnoreExtension.Location = new Point(12 + chkTuningSchema.Width + 16 + chkIncludeOwner.Width + 16, 54);
                    
                    // Position buttons on row 1
                    int currentX = 12 + 160 + 8;
                    if (btnOpenSchemaFolder.Visible) {
                        btnOpenSchemaFolder.Location = new Point(currentX, 12);
                        currentX += 48;
                    }
                    if (btnEditSchema.Visible) {
                        btnEditSchema.Location = new Point(currentX, 12);
                        currentX += 48;
                    }
                    
                    pnlStatusLabels.Location = new Point(currentX + 16, 14);
                    int availableWidth = Math.Max(100, pnlActionBar.Width - currentX - 16 - 12);
                    pnlStatusLabels.Width = availableWidth;
                    
                    int prefHeight = pnlStatusLabels.GetPreferredSize(new Size(availableWidth, 0)).Height;
                    int requiredPanelHeight = Math.Max(90, prefHeight + 28);
                    
                    pnlStatusLabels.Height = prefHeight;
                    if (pnlActionBar.Height != requiredPanelHeight) {
                        pnlActionBar.MinimumSize = new Size(0, requiredPanelHeight);
                        pnlActionBar.Height = requiredPanelHeight;
                    }
                } else {
                    // All on row 1
                    int currentX = startX;
                    if (btnOpenSchemaFolder.Visible) {
                        btnOpenSchemaFolder.Location = new Point(12 + 160 + 8, 12);
                    }
                    if (btnEditSchema.Visible) {
                        btnEditSchema.Location = new Point(12 + 160 + 8 + (btnOpenSchemaFolder.Visible ? 48 : 0), 12);
                    }
                    
                    chkTuningSchema.Location = new Point(currentX, 19);
                    currentX += chkTuningSchema.Width + 16;
                    
                    chkIncludeOwner.Location = new Point(currentX, 19);
                    currentX += chkIncludeOwner.Width + 16;
                    
                    chkIgnoreExtension.Location = new Point(currentX, 19);
                    currentX += chkIgnoreExtension.Width + 16;
                    
                    pnlStatusLabels.Location = new Point(currentX + 16, 14);
                    int availableWidth = Math.Max(100, pnlActionBar.Width - currentX - 16 - 12);
                    pnlStatusLabels.Width = availableWidth;
                    
                    int prefHeight = pnlStatusLabels.GetPreferredSize(new Size(availableWidth, 0)).Height;
                    int requiredPanelHeight = Math.Max(62, prefHeight + 28);
                    
                    pnlStatusLabels.Height = prefHeight;
                    if (pnlActionBar.Height != requiredPanelHeight) {
                        pnlActionBar.MinimumSize = new Size(0, requiredPanelHeight);
                        pnlActionBar.Height = requiredPanelHeight;
                    }
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
                var lblDb = new Label { Text = title + " DB:", Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold), ForeColor = color, TextAlign = ContentAlignment.BottomLeft, AutoSize = true, Dock = DockStyle.Bottom };
                if (title == "SOURCE") lblSourceDataDbTitle = lblDb;
                else if (title == "TARGET") lblTargetDataDbTitle = lblDb;
                p.Controls.Add(lblDb, 0, 0);
                p.Controls.Add(db, 0, 1);
                p.Controls.Add(new Label { Text = title + " SCHEMA:", Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold), ForeColor = color, TextAlign = ContentAlignment.BottomLeft, AutoSize = true, Dock = DockStyle.Bottom }, 1, 0);
                p.Controls.Add(schema, 1, 1);
                parent.Controls.Add(p, col, 0);
                db.GotFocus += (s, e) => { db.BackColor = Color.FromArgb(232, 242, 252); };
                db.LostFocus += (s, e) => { db.BackColor = Color.White; };
                schema.GotFocus += (s, e) => { schema.BackColor = Color.FromArgb(232, 242, 252); };
                schema.LostFocus += (s, e) => { schema.BackColor = Color.White; };
            }

            cmbSourceDataDb = new ComboBox { Name = "cmbSourceDataDb" }; cmbSourceDataSchema = new ComboBox { Name = "cmbSourceDataSchema" };
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

            cmbTargetDataDb = new ComboBox { Name = "cmbTargetDataDb" }; cmbTargetDataSchema = new ComboBox { Name = "cmbTargetDataSchema" };
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
            
            chkTuningData = new CheckBox { Text = "Tuning Script (Safe Run)", AutoSize = true, Margin = new Padding(15, 5, 0, 0), Checked = false, Font = new Font(UIConstants.MainFontName, 8.5f), ForeColor = UIConstants.TextSecondary };
            tooltip.SetToolTip(chkTuningData, "Ensure output scripts contain idempotent checks (e.g. ON CONFLICT DO NOTHING / UPDATE) to prevent errors on different target databases.");
            pnlDataOptions.Controls.Add(chkTuningData);
            
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

            pnlDataStatusLabels = new FlowLayoutPanel { 
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(8, 2, 0, 0),
                Padding = new Padding(0),
                WrapContents = false,
                Height = 32
            };
            pnlDataActions.Controls.Add(pnlDataStatusLabels);

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

            chkDryRun = new CheckBox {
                Text = "Dry Run (Rollback)",
                AutoSize = true,
                Margin = new Padding(0, 10, 16, 0),
                Font = new Font(UIConstants.MainFontName, 9.5f),
                ForeColor = UIConstants.Danger,
                Cursor = Cursors.Hand
            };

            pnlToolbar.Controls.AddRange(new Control[] { lblStatus, chkDryRun, btnExecuteSchema, btnExecuteData, btnVerifySync });
            
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
            dgvJunkDataResults.ColumnHeaderMouseClick += DgvJunkDataResults_ColumnHeaderMouseClick;

            cmbJunkConnection.SelectedIndexChanged += async (s, e) => {
                if (_suppressComboEvents) return;
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

            var cardFinal = CreateCardPanel("FINAL DATABASE EXPORT", 800, 230);
            cardFinal.Dock = DockStyle.Top;

            var pnlFinalGrid = new TableLayoutPanel {
                Location = new Point(15, 40),
                Width = 750,
                Height = 80,
                ColumnCount = 3,
                RowCount = 2
            };
            pnlFinalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            pnlFinalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            pnlFinalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlFinalGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            pnlFinalGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var lblExportConn = new Label { Text = "Connection:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            cmbFinalExportConnection = new ComboBox { Name = "cmbFinalExportConnection", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UIConstants.MainFontName, 9f) };
            cmbFinalExportConnection.Items.AddRange(new string[] { "Source (Dev)", "Target (Prod)", "Custom Connection..." });
            cmbFinalExportConnection.SelectedIndex = 1; // Default to Target (Prod)

            var lblExportDb = new Label { Text = "Target Databases:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            
            var pnlDbButtons = new FlowLayoutPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 26, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            var btnSelectAllDbs = new Button { Text = "✔  All", Width = 70, Height = 26, Margin = new Padding(0, 0, 8, 0) };
            StyleButtonSecondary(btnSelectAllDbs); btnSelectAllDbs.Font = new Font(UIConstants.MainFontName, 8.5f);
            btnSelectAllDbs.Click += (s, e) => {
                for (int i = 0; i < clbFinalExportDbs.Items.Count; i++)
                    clbFinalExportDbs.SetItemChecked(i, true);
            };

            var btnUnselectAllDbs = new Button { Text = "✖  None", Width = 70, Height = 26, Margin = new Padding(0, 0, 0, 0) };
            StyleButtonSecondary(btnUnselectAllDbs); btnUnselectAllDbs.Font = new Font(UIConstants.MainFontName, 8.5f);
            btnUnselectAllDbs.Click += (s, e) => {
                for (int i = 0; i < clbFinalExportDbs.Items.Count; i++)
                    clbFinalExportDbs.SetItemChecked(i, false);
            };
            pnlDbButtons.Controls.AddRange(new Control[] { btnSelectAllDbs, btnUnselectAllDbs });

            var btnExportFinal = new Button { Text = "📦  Export (Backup + Sql)", Width = 230, Height = 40, Anchor = AnchorStyles.Right | AnchorStyles.None, Margin = new Padding(0, 0, 0, 0) };
            StyleButtonPrimary(btnExportFinal); btnExportFinal.Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold);
            btnExportFinal.Click += BtnExportFinal_Click;

            pnlFinalGrid.Controls.Add(lblExportConn, 0, 0);
            pnlFinalGrid.Controls.Add(cmbFinalExportConnection, 1, 0);
            pnlFinalGrid.Controls.Add(lblExportDb, 0, 1);
            pnlFinalGrid.Controls.Add(pnlDbButtons, 1, 1);
            pnlFinalGrid.Controls.Add(btnExportFinal, 2, 0);
            pnlFinalGrid.SetRowSpan(btnExportFinal, 2);

            cardFinal.Controls.Add(pnlFinalGrid);

            clbFinalExportDbs = new CheckedListBox {
                Name = "clbFinalExportDbs",
                Location = new Point(15, 128),
                Width = 760,
                Height = 84,
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(UIConstants.MainFontName, 9f),
                MultiColumn = true,
                ColumnWidth = 180
            };
            cardFinal.Controls.Add(clbFinalExportDbs);

            cmbFinalExportConnection.SelectedIndexChanged += async (s, e) => {
                if (_suppressComboEvents) return;
                if (cmbFinalExportConnection.SelectedIndex == 2) // Custom
                {
                    using (var dlg = new ConnectionDialog("Custom Database Connection", _customFinalExportConfig))
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            _customFinalExportConfig = dlg.Config;
                            _customFinalExportPgService = new PostgresService(_customFinalExportConfig!) { PostgresBinPath = txtPgBinPath.Text };
                            await LoadFinalExportDbsAsync();
                        }
                    }
                }
                else
                {
                    await LoadFinalExportDbsAsync();
                }
            };

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
                var contentWidth = Math.Max(200, w - 30);
                pnlFinalGrid.Width = contentWidth;
                clbFinalExportDbs.Width = contentWidth;
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
            var cardConfigSetup = CreateCardPanel("ENVIRONMENT CONFIGURATION COMPARISON", 800, 249);
            cardConfigSetup.Dock = DockStyle.Top;

            rbCompareFile = new RadioButton { Text = "Compare Files", Location = new Point(15, 46), AutoSize = true, Checked = true, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            rbCompareFolder = new RadioButton { Text = "Compare Folders", Location = new Point(155, 46), AutoSize = true, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary };
            cardConfigSetup.Controls.Add(rbCompareFile);
            cardConfigSetup.Controls.Add(rbCompareFolder);

            var pnlConfigBody = new TableLayoutPanel {
                Location = new Point(15, 76), Width = 760, Height = 110,
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

            btnCompareConfig = new Button { Text = "\u2194  Analyze Configuration Differences", Width = 300, Height = 36, Location = new Point(15, 202) };
            StyleButtonPrimary(btnCompareConfig); btnCompareConfig.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);

            cardConfigSetup.Controls.Add(pnlConfigBody);
            cardConfigSetup.Controls.Add(btnCompareConfig);

            // Responsive
            void UpdateConfigWidths() {
                var w = Math.Max(400, pnlConfigMain.ClientSize.Width - pnlConfigMain.Padding.Horizontal);
                cardConfigSetup.Width = w;
                pnlConfigBody.Width = Math.Max(200, cardConfigSetup.ClientSize.Width - 30);
                btnCompareConfig.Location = new Point(15, 202);
            }
            pnlConfigMain.SizeChanged += (s, e) => UpdateConfigWidths();
            pnlConfigMain.HandleCreated += (s, e) => UpdateConfigWidths();
            cardConfigSetup.SizeChanged += (s, e) => pnlConfigBody.Width = Math.Max(200, cardConfigSetup.ClientSize.Width - 30);

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

            rbCompareFile.CheckedChanged += (s, e) => {
                if (rbCompareFile.Checked) {
                    lblOldConfig.Text = "Source Config File:";
                    lblNewConfig.Text = "Target Config File:";
                    btnSelectOldConfig.Text = "\uD83D\uDCC1  Browse Source Config";
                    btnSelectNewConfig.Text = "\uD83D\uDCC1  Browse Target Config";
                    if (!_suppressConfigEvents) {
                        _suppressConfigEvents = true;
                        try {
                            txtOldConfigPath.Text = _configCompareSourceFile;
                            txtNewConfigPath.Text = _configCompareTargetFile;
                        } finally {
                            _suppressConfigEvents = false;
                        }
                        SaveConfig();
                    }
                }
            };
            rbCompareFolder.CheckedChanged += (s, e) => {
                if (rbCompareFolder.Checked) {
                    lblOldConfig.Text = "Source Config Folder:";
                    lblNewConfig.Text = "Target Config Folder:";
                    btnSelectOldConfig.Text = "\uD83D\uDCC1  Browse Source Folder";
                    btnSelectNewConfig.Text = "\uD83D\uDCC1  Browse Target Folder";
                    if (!_suppressConfigEvents) {
                        _suppressConfigEvents = true;
                        try {
                            txtOldConfigPath.Text = _configCompareSourceFolder;
                            txtNewConfigPath.Text = _configCompareTargetFolder;
                        } finally {
                            _suppressConfigEvents = false;
                        }
                        SaveConfig();
                    }
                }
            };

            btnSelectOldConfig.Click += (s, e) => {
                if (rbCompareFolder.Checked) {
                    SelectFolder(txtOldConfigPath);
                } else {
                    SelectFile(txtOldConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
                }
            };
            btnSelectNewConfig.Click += (s, e) => {
                if (rbCompareFolder.Checked) {
                    SelectFolder(txtNewConfigPath);
                } else {
                    SelectFile(txtNewConfigPath, "JSON files (*.json)|*.json|ENV files (*.env)|*.env");
                }
            };
            btnCompareConfig.Click += BtnCompareConfig_Click;

            txtOldConfigPath.TextChanged += (s, e) => {
                if (!_suppressConfigEvents) {
                    if (rbCompareFolder.Checked) {
                        _configCompareSourceFolder = txtOldConfigPath.Text;
                    } else {
                        _configCompareSourceFile = txtOldConfigPath.Text;
                    }
                    SaveConfig();
                }
            };
            txtNewConfigPath.TextChanged += (s, e) => {
                if (!_suppressConfigEvents) {
                    if (rbCompareFolder.Checked) {
                        _configCompareTargetFolder = txtNewConfigPath.Text;
                    } else {
                        _configCompareTargetFile = txtNewConfigPath.Text;
                    }
                    SaveConfig();
                }
            };

            // 9. AI Review Tab
            var tabAi = new TabPage("9. AI Review") { BackColor = Color.White };
            var pnlAiMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            var lblAiIntro = new Label {
                Text = "Utilize AI models to audit and validate database schema changes and configuration differences for potential issues.",
                Dock = DockStyle.Top, Height = 42,
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            var pnlAiActionHost = new Panel { Dock = DockStyle.Top, Height = 162, BackColor = Color.White };
            var cardAiActions = CreateCardPanel("AI-POWERED VALIDATION", 800, 154);
            cardAiActions.Dock = DockStyle.Fill;
            var pnlAiBtns = new FlowLayoutPanel { Width = 570, Height = 40, FlowDirection = FlowDirection.LeftToRight, Location = new Point(15, 76), WrapContents = false };
            var pnlAiReadiness = new Panel { Width = 310, Height = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            lblAiKeyReadiness = new Label { AutoSize = false, Width = 300, Height = 20, Location = new Point(0, 0), Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold) };
            lblAiSchemaReadiness = new Label { AutoSize = false, Width = 300, Height = 20, Location = new Point(0, 22), Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold) };
            lblAiDataReadiness = new Label { AutoSize = false, Width = 300, Height = 20, Location = new Point(0, 44), Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold) };
            lblAiConfigReadiness = new Label { AutoSize = false, Width = 300, Height = 20, Location = new Point(0, 66), Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold) };
            
            btnReviewSchema = new Button { Text = "\u2728  Review Schema Changes", Width = 180, Height = 34, Margin = new Padding(0, 0, 12, 0) };
            StyleButtonPrimary(btnReviewSchema); btnReviewSchema.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            
            btnReviewData = new Button { Text = "\u2728  Review Data Changes", Width = 180, Height = 34, Margin = new Padding(0, 0, 12, 0) };
            StyleButtonPrimary(btnReviewData); btnReviewData.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            
            btnReviewConfig = new Button { Text = "\u2728  Audit Configuration Diff", Width = 180, Height = 34, Margin = new Padding(0, 0, 0, 0) };
            StyleButtonPrimary(btnReviewConfig); btnReviewConfig.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            
            btnReviewSchema.Click += BtnReviewSchema_Click;
            btnReviewData.Click += BtnReviewData_Click;
            btnReviewConfig.Click += BtnReviewConfig_Click;
            tooltip.SetToolTip(btnReviewSchema, "Validate the generated schema SQL script with AI.");
            tooltip.SetToolTip(btnReviewData, "Validate the generated data sync SQL script with AI.");
            tooltip.SetToolTip(btnReviewConfig, "Audit configuration differences from the Config Compare tab with AI.");
 
            pnlAiBtns.Controls.Add(btnReviewSchema);
            pnlAiBtns.Controls.Add(btnReviewData);
            pnlAiBtns.Controls.Add(btnReviewConfig);
            cardAiActions.Controls.Add(pnlAiBtns);
            pnlAiReadiness.Controls.Add(lblAiKeyReadiness);
            pnlAiReadiness.Controls.Add(lblAiSchemaReadiness);
            pnlAiReadiness.Controls.Add(lblAiDataReadiness);
            pnlAiReadiness.Controls.Add(lblAiConfigReadiness);
            cardAiActions.Controls.Add(pnlAiReadiness);
            pnlAiActionHost.Controls.Add(cardAiActions);
 
            var spacerAi1 = new Panel { Dock = DockStyle.Top, Height = 12 };
            var lblAiLogHeader = new Label {
                Text = "\uD83D\u2728  AI AUDIT LOG", Dock = DockStyle.Top, Height = 28,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };
 
            lblAiReviewStatus = new Label {
                Text = "Ready - Choose an AI review action",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                TextAlign = ContentAlignment.MiddleLeft
            };
 
            var pnlAiLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtAiReviewLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtAiReviewLog);
            txtAiReviewLog.Text = "No AI output yet.\r\n\r\nNext steps:\r\n1) Click 'Review Schema Changes' after generating schema script (Tab 3).\r\n2) Click 'Review Data Changes' after generating data sync script (Tab 4).\r\n3) Click 'Audit Configuration Diff' after running config compare (Tab 8).";
            pnlAiLog.Controls.Add(txtAiReviewLog);
 
            // Responsive widths
            void UpdateAiWidths() {
                var w = Math.Max(400, pnlAiMain.ClientSize.Width - pnlAiMain.Padding.Horizontal);
                cardAiActions.Width = w;
                var showReadiness = w >= 980;
                pnlAiReadiness.Visible = showReadiness;
                pnlAiReadiness.Location = new Point(Math.Max(15, cardAiActions.ClientSize.Width - pnlAiReadiness.Width - 18), 54);
                
                var maxButtonsWidth = showReadiness ? Math.Max(560, cardAiActions.ClientSize.Width - pnlAiReadiness.Width - 52) : Math.Max(200, cardAiActions.ClientSize.Width - 30);
                var actionButtonWidth = Math.Max(180, Math.Min(280, (maxButtonsWidth - 24) / 3));
                var buttonsTotalWidth = 3 * actionButtonWidth + 24;
                
                pnlAiBtns.Width = buttonsTotalWidth;
                pnlAiBtns.Location = new Point(15 + (maxButtonsWidth - buttonsTotalWidth) / 2, 76);
                
                btnReviewSchema.Width = actionButtonWidth;
                btnReviewData.Width = actionButtonWidth;
                btnReviewConfig.Width = actionButtonWidth;
            }
            pnlAiMain.SizeChanged += (s, e) => UpdateAiWidths();
            pnlAiMain.HandleCreated += (s, e) => UpdateAiWidths();

            pnlAiMain.Controls.Add(pnlAiLog);          // Fill (bottom priority, added first)
            pnlAiMain.Controls.Add(lblAiReviewStatus); // Top
            pnlAiMain.Controls.Add(lblAiLogHeader);    // Top
            pnlAiMain.Controls.Add(spacerAi1);
            pnlAiMain.Controls.Add(pnlAiActionHost);    // Top
            pnlAiMain.Controls.Add(lblAiIntro);         // Top (topmost priority, added last)
            
            tabAi.Controls.Add(pnlAiMain);

            // 10. Other Tab
            var tabOther = new TabPage("10. Other") { BackColor = Color.White };
            var pnlOtherMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };

            var lblOtherIntro = new Label {
                Text = "Convert SQL script files by safe-tuning statement syntax or filtering specific components like owner, privileges, tablespaces, and comments.",
                Dock = DockStyle.Top, Height = 42,
                Font = new Font(UIConstants.MainFontName, 9.5f), ForeColor = UIConstants.TextSecondary
            };

            var cardOtherSetup = CreateCardPanel("SQL SCRIPT CONVERTER", 800, 310);
            cardOtherSetup.Dock = DockStyle.Top;

            var pnlOtherGrid = new TableLayoutPanel {
                Location = new Point(15, 40),
                Width = 760,
                Height = 255,
                ColumnCount = 3,
                RowCount = 7,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlOtherGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            pnlOtherGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlOtherGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            pnlOtherGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            // Row 0: Source File Label, TextBox & Browse button
            var lblConvertSrc = new Label { Text = "Source SQL File:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary, Margin = new Padding(0, 4, 8, 0) };
            txtConvertSourceFile = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true, BackColor = UIConstants.Surface, Font = new Font(UIConstants.MainFontName, 9f), BorderStyle = BorderStyle.FixedSingle, Height = 28, Margin = new Padding(0, 3, 0, 0) };
            btnBrowseConvertFile = new Button { Text = "Browse...", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(8, 2, 0, 0), Font = new Font(UIConstants.MainFontName, 9f), Height = 28 };
            StyleButtonSecondary(btnBrowseConvertFile);

            // Row 1: Checkbox: Tuning Script (Safe Run)
            var pnlCheckboxRowTuning = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 28 };
            chkConvertTuning = new CheckBox { Text = "Tuning Script (Safe Run)", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            pnlCheckboxRowTuning.Controls.Add(chkConvertTuning);

            // Row 2: Checkboxes: Ignore OWNER TO, Ignore Tablespaces, Ignore Comments
            var pnlCheckboxRow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 28 };
            chkIgnoreOwner = new CheckBox { Text = "Ignore OWNER TO", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreTablespaces = new CheckBox { Text = "Ignore Tablespaces", Checked = false, AutoSize = false, Width = 200, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreComments = new CheckBox { Text = "Ignore Comments", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 0, 0) };
            pnlCheckboxRow2.Controls.AddRange(new Control[] { chkIgnoreOwner, chkIgnoreTablespaces, chkIgnoreComments });

            // Row 3: Checkboxes: Ignore Privileges, Ignore Subscriptions, Ignore Security Labels
            var pnlCheckboxRow3 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 28 };
            chkIgnorePrivileges = new CheckBox { Text = "Ignore Privileges (GRANT/REVOKE)", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreSubscriptions = new CheckBox { Text = "Ignore Subscriptions", Checked = false, AutoSize = false, Width = 200, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreSecurityLabels = new CheckBox { Text = "Ignore Security Labels", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 0, 0) };
            pnlCheckboxRow3.Controls.AddRange(new Control[] { chkIgnorePrivileges, chkIgnoreSubscriptions, chkIgnoreSecurityLabels });

            // Row 4: Checkboxes: Ignore Publications, Ignore Data, Ignore Schema
            var pnlCheckboxRow4 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 28 };
            chkIgnorePublications = new CheckBox { Text = "Ignore Publications", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreData = new CheckBox { Text = "Ignore Data (INSERT/COPY)", Checked = false, AutoSize = false, Width = 200, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreSchema = new CheckBox { Text = "Ignore Schema (CREATE/DROP SCHEMA)", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 0, 0) };
            pnlCheckboxRow4.Controls.AddRange(new Control[] { chkIgnorePublications, chkIgnoreData, chkIgnoreSchema });

            // Row 5: Checkbox: Ignore Table Access Methods, Ignore Transaction
            var pnlCheckboxRow5 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 28 };
            chkIgnoreTableAccessMethods = new CheckBox { Text = "Ignore Table Access Methods (USING)", Checked = false, AutoSize = false, Width = 260, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 20, 0) };
            chkIgnoreTransaction = new CheckBox { Text = "Ignore Transaction (BEGIN/COMMIT/END)", Checked = false, AutoSize = false, Width = 300, Height = 24, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary, Margin = new Padding(0, 2, 0, 0) };
            pnlCheckboxRow5.Controls.AddRange(new Control[] { chkIgnoreTableAccessMethods, chkIgnoreTransaction });

            var pnlConvertActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), WrapContents = false, Height = 42 };
            btnAnalyzeScript = new Button { Text = "🔍  Analyze Script", Width = 160, Height = 38, Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0) };
            StyleButtonSecondary(btnAnalyzeScript);
            btnReviewScript = new Button { Text = "📝  Review Changes", Width = 180, Height = 38, Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0), Enabled = false };
            StyleButtonSecondary(btnReviewScript);
            btnConvertScript = new Button { Text = "⚡  Convert SQL Script", Width = 200, Height = 38, Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0) };
            StyleButtonPrimary(btnConvertScript);
            btnOpenConvertFolder = new Button { Text = "📂  Open Folder", Width = 140, Height = 38, Font = new Font(UIConstants.MainFontName, 9f), Enabled = false, Margin = new Padding(0) };
            StyleButtonSecondary(btnOpenConvertFolder);
            pnlConvertActions.Controls.AddRange(new Control[] { btnAnalyzeScript, btnReviewScript, btnConvertScript, btnOpenConvertFolder });

            pnlOtherGrid.Controls.Add(lblConvertSrc, 0, 0);
            pnlOtherGrid.Controls.Add(txtConvertSourceFile, 1, 0);
            pnlOtherGrid.Controls.Add(btnBrowseConvertFile, 2, 0);
            
            pnlOtherGrid.Controls.Add(new Label { Text = "Tuning Options:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary, Margin = new Padding(0, 4, 8, 0) }, 0, 1);
            pnlOtherGrid.Controls.Add(pnlCheckboxRowTuning, 1, 1);
            
            pnlOtherGrid.Controls.Add(new Label { Text = "Filters (Ignore):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextSecondary, Margin = new Padding(0, 4, 8, 0) }, 0, 2);
            pnlOtherGrid.Controls.Add(pnlCheckboxRow2, 1, 2);
            pnlOtherGrid.Controls.Add(pnlCheckboxRow3, 1, 3);
            pnlOtherGrid.Controls.Add(pnlCheckboxRow4, 1, 4);
            pnlOtherGrid.Controls.Add(pnlCheckboxRow5, 1, 5);
            pnlOtherGrid.Controls.Add(pnlConvertActions, 1, 6);

            cardOtherSetup.Controls.Add(pnlOtherGrid);

            var spacerOther1 = new Panel { Dock = DockStyle.Top, Height = 16 };

            var lblOtherLogHeader = new Label {
                Text = "📋  CONVERSION LOG", Dock = DockStyle.Top, Height = 28,
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold), ForeColor = UIConstants.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var pnlOtherLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UIConstants.ConsoleBg };
            txtConvertLog = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            StyleTextBoxConsole(txtConvertLog);
            pnlOtherLog.Controls.Add(txtConvertLog);

            pnlOtherMain.Controls.Add(pnlOtherLog);        // Fill
            pnlOtherMain.Controls.Add(lblOtherLogHeader);    // Top
            pnlOtherMain.Controls.Add(spacerOther1);        // Spacer
            pnlOtherMain.Controls.Add(cardOtherSetup);      // Top
            pnlOtherMain.Controls.Add(lblOtherIntro);        // Top

            tabOther.Controls.Add(pnlOtherMain);

            // Responsive widths for Tab 10
            void UpdateOtherWidths() {
                var w = Math.Max(400, pnlOtherMain.ClientSize.Width - pnlOtherMain.Padding.Horizontal);
                cardOtherSetup.Width = w;
                pnlOtherGrid.Width = Math.Max(200, w - 30);
            }
            pnlOtherMain.SizeChanged += (s, e) => UpdateOtherWidths();
            pnlOtherMain.HandleCreated += (s, e) => UpdateOtherWidths();

            // Wire Tab 10 events
            btnBrowseConvertFile.Click += (s, e) => {
                using (var ofd = new OpenFileDialog { Filter = "SQL Script Files (*.sql)|*.sql|All Files (*.*)|*.*" }) {
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtConvertSourceFile.Text = ofd.FileName;
                        SaveConfig();
                    }
                }
            };

            btnConvertScript.Click += async (s, e) => {
                string srcPath = txtConvertSourceFile.Text;
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath)) {
                    MessageBox.Show("Please select a valid source SQL script file first.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnConvertScript.Text = "⌛ Converting...";
                btnConvertScript.Enabled = false;
                txtConvertLog.Clear();
                txtConvertLog.AppendText($"Starting script conversion on: {srcPath}\r\n");

                try {
                    // Read file
                    string rawSql = await File.ReadAllTextAsync(srcPath);
                    txtConvertLog.AppendText($"Read {rawSql.Length} characters of source SQL script.\r\n");

                    // Run convert service
                    string targetSchema = cmbTargetSchema.Text;
                    if (string.IsNullOrEmpty(targetSchema)) targetSchema = "public";

                    bool tuneScript = chkConvertTuning.Checked;
                    bool ignoreOwner = chkIgnoreOwner.Checked;
                    bool ignorePrivileges = chkIgnorePrivileges.Checked;
                    bool ignoreTablespaces = chkIgnoreTablespaces.Checked;
                    bool ignoreComments = chkIgnoreComments.Checked;
                    bool ignorePublications = chkIgnorePublications.Checked;
                    bool ignoreSubscriptions = chkIgnoreSubscriptions.Checked;
                    bool ignoreSecurityLabels = chkIgnoreSecurityLabels.Checked;
                    bool ignoreTableAccessMethods = chkIgnoreTableAccessMethods.Checked;
                    bool ignoreData = chkIgnoreData.Checked;
                    bool ignoreSchema = chkIgnoreSchema.Checked;
                    bool ignoreTransaction = chkIgnoreTransaction.Checked;

                    var convertResult = await Task.Run(() => SqlTuningHelper.ConvertScript(
                        rawSql,
                        tuneScript,
                        targetSchema,
                        ignoreOwner,
                        ignorePrivileges,
                        ignoreTablespaces,
                        ignoreComments,
                        ignorePublications,
                        ignoreSubscriptions,
                        ignoreSecurityLabels,
                        ignoreTableAccessMethods,
                        ignoreData,
                        ignoreSchema,
                        ignoreTransaction
                    ));

                    // Write result to file
                    string dir = Path.GetDirectoryName(srcPath) ?? "";
                    string ext = Path.GetExtension(srcPath);
                    string name = Path.GetFileNameWithoutExtension(srcPath);
                    string targetPath = Path.Combine(dir, $"{name}_converted{ext}");

                    await File.WriteAllTextAsync(targetPath, convertResult.ConvertedSql);

                    // Display conversion log
                    string convLog = convertResult.BuildLog(
                        tuneScript, targetSchema,
                        ignoreOwner, ignorePrivileges, ignoreTablespaces,
                        ignoreComments, ignorePublications, ignoreSubscriptions,
                        ignoreSecurityLabels, ignoreTableAccessMethods,
                        ignoreData, ignoreSchema, ignoreTransaction);
                    txtConvertLog.AppendText(convLog);

                    txtConvertLog.AppendText($"\r\nConversion finished!\r\n");
                    txtConvertLog.AppendText($"Saved to: {targetPath}\r\n");

                    _lastConvertedScriptPath = targetPath;
                    btnOpenConvertFolder.Enabled = true;

                    MessageBox.Show($"Script conversion completed successfully!\n\nSaved to: {targetPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) {
                    txtConvertLog.AppendText($"\r\n❌ Error: {ex.Message}\r\n");
                    MessageBox.Show($"Error during script conversion: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally {
                    btnConvertScript.Text = "⚡  Convert SQL Script";
                    btnConvertScript.Enabled = true;
                }
            };

            btnAnalyzeScript.Click += async (s, e) => {
                string srcPath = txtConvertSourceFile.Text;
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath)) {
                    MessageBox.Show("Please select a valid source SQL script file first.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnAnalyzeScript.Text = "⌛ Analyzing...";
                btnAnalyzeScript.Enabled = false;
                txtConvertLog.Clear();
                txtConvertLog.AppendText($"Starting script analysis on: {srcPath}\r\n\r\n");

                try {
                    // Read file
                    string rawSql = await File.ReadAllTextAsync(srcPath);
                    txtConvertLog.AppendText($"Read {rawSql.Length} characters of source SQL script.\r\n");

                    // Run convert service in dry run mode
                    string targetSchema = cmbTargetSchema.Text;
                    if (string.IsNullOrEmpty(targetSchema)) targetSchema = "public";

                    string report = await Task.Run(() => SqlTuningHelper.AnalyzeScript(
                        rawSql,
                        chkConvertTuning.Checked,
                        targetSchema,
                        chkIgnoreOwner.Checked,
                        chkIgnorePrivileges.Checked,
                        chkIgnoreTablespaces.Checked,
                        chkIgnoreComments.Checked,
                        chkIgnorePublications.Checked,
                        chkIgnoreSubscriptions.Checked,
                        chkIgnoreSecurityLabels.Checked,
                        chkIgnoreTableAccessMethods.Checked,
                        chkIgnoreData.Checked,
                        chkIgnoreSchema.Checked,
                        chkIgnoreTransaction.Checked
                    ));

                    txtConvertLog.AppendText(report);
                    btnReviewScript.Enabled = true;
                }
                catch (Exception ex) {
                    txtConvertLog.AppendText($"\r\n❌ Error: {ex.Message}\r\n");
                    MessageBox.Show($"Error during script analysis: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally {
                    btnAnalyzeScript.Text = "🔍  Analyze Script";
                    btnAnalyzeScript.Enabled = true;
                }
            };

            btnReviewScript.Click += async (s, e) => {
                string srcPath = txtConvertSourceFile.Text;
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath)) {
                    MessageBox.Show("Please select a valid source SQL script file first.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnReviewScript.Text = "⌛ Loading Preview...";
                btnReviewScript.Enabled = false;

                try {
                    string rawSql = await File.ReadAllTextAsync(srcPath);
                    string targetSchema = cmbTargetSchema.Text;
                    if (string.IsNullOrEmpty(targetSchema)) targetSchema = "public";

                    var convertResult = await Task.Run(() => SqlTuningHelper.ConvertScript(
                        rawSql,
                        chkConvertTuning.Checked,
                        targetSchema,
                        chkIgnoreOwner.Checked,
                        chkIgnorePrivileges.Checked,
                        chkIgnoreTablespaces.Checked,
                        chkIgnoreComments.Checked,
                        chkIgnorePublications.Checked,
                        chkIgnoreSubscriptions.Checked,
                        chkIgnoreSecurityLabels.Checked,
                        chkIgnoreTableAccessMethods.Checked,
                        chkIgnoreData.Checked,
                        chkIgnoreSchema.Checked,
                        chkIgnoreTransaction.Checked
                    ));

                    string summaryReport = await Task.Run(() => SqlTuningHelper.AnalyzeScript(
                        rawSql,
                        chkConvertTuning.Checked,
                        targetSchema,
                        chkIgnoreOwner.Checked,
                        chkIgnorePrivileges.Checked,
                        chkIgnoreTablespaces.Checked,
                        chkIgnoreComments.Checked,
                        chkIgnorePublications.Checked,
                        chkIgnoreSubscriptions.Checked,
                        chkIgnoreSecurityLabels.Checked,
                        chkIgnoreTableAccessMethods.Checked,
                        chkIgnoreData.Checked,
                        chkIgnoreSchema.Checked,
                        chkIgnoreTransaction.Checked
                    ));

                    using (var reviewDlg = new SqlReviewDialog(rawSql, convertResult.ConvertedSql, summaryReport)) {
                        reviewDlg.ShowDialog(this);
                    }
                }
                catch (Exception ex) {
                    MessageBox.Show($"Error loading preview: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally {
                    btnReviewScript.Text = "📝  Review Changes";
                    btnReviewScript.Enabled = true;
                }
            };

            btnOpenConvertFolder.Click += (s, e) => {
                if (!string.IsNullOrEmpty(_lastConvertedScriptPath) && File.Exists(_lastConvertedScriptPath)) {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastConvertedScriptPath.Replace("/", "\\")}\"") { UseShellExecute = true });
                }
            };

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
            tabControl.TabPages.Add(tabOther);            // 10

            tabControl.SelectedIndexChanged += async (s, e) => {
                if ((tabControl.SelectedTab == tabCompareSchema || tabControl.SelectedTab == tabCompareData) && cmbSourceDb.Items.Count == 0)
                    await LoadDatabaseListsAsync();
                if (tabControl.SelectedTab == tabAi)
                    UpdateAiReadinessStatus();
            };
            
            this.FormClosing += (s, e) => SaveConfig();
            this.Load += async (s, e) => {
                LoadConfig();
                await RefreshModelQuotaStatusAsync();
                UpdateAiReadinessStatus();
                
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
                _dbCompareService = new DatabaseCompareService(_newDbConfig!, _oldDbConfig!);
                if (chkIgnoreExtension != null) _dbCompareService.IgnoreExtension = chkIgnoreExtension.Checked;
                _fileSystemService = new FileSystemService(txtReleasePath.Text, txtReleaseVersion.Text, txtProductName.Text);
                var provider = cmbAiProvider.SelectedItem?.ToString() ?? "Gemini";
                var model = string.IsNullOrWhiteSpace(cmbAiModel.Text) ? "gemini-2.0-flash" : cmbAiModel.Text.Split(' ')[0];
                _aiService = new AIOperationService(txtAiKey.Text, model, provider);
                _junkService = new JunkAnalysisService(_oldPgService);

                _fileSystemService.EnsureDirectoryStructure();
                UpdateAiReadinessStatus();
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
                _dbCompareService = new DatabaseCompareService(_newDbConfig!, _oldDbConfig!);
                if (chkIgnoreExtension != null) _dbCompareService.IgnoreExtension = chkIgnoreExtension.Checked;
                _fileSystemService = new FileSystemService(txtReleasePath.Text, txtReleaseVersion.Text, txtProductName.Text);
                var provider = cmbAiProvider.SelectedItem?.ToString() ?? "Gemini";
                var model = string.IsNullOrWhiteSpace(cmbAiModel.Text) ? "gemini-2.0-flash" : cmbAiModel.Text.Split(' ')[0];
                _aiService = new AIOperationService(txtAiKey.Text, model, provider);

                _fileSystemService.EnsureDirectoryStructure();
                SaveConfig(); 
                UpdateAiReadinessStatus();
                await LoadDatabaseListsAsync(); // Auto-load DB lists for comparison
                MessageBox.Show($"Services initialized and directory structure created at {_fileSystemService.BaseReleasePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing services: {ex.Message}");
            }
        }

        private async void RestoreDbAsync(PostgresService? service, string dbName, string targetDbName, string filePath)
        {
            if (!EnsureServicesInitialized()) return;

            // Clear previous restore logs and reset filters to default to show new logs immediately
            _restoreLogLines.Clear();
            if (txtBackupLog != null) txtBackupLog.Clear();
            if (txtLogFilter != null) txtLogFilter.Text = "";
            if (cmbLogFilterType != null && cmbLogFilterType.Items.Count > 0) cmbLogFilterType.SelectedIndex = 0;

            _sqlFileStatuses.Clear();
            foreach (var f in _postRestoreSqlFiles) {
                _sqlFileStatuses[f] = "pending";
            }
            if (lstPostRestoreSqls != null) {
                lstPostRestoreSqls.Invalidate();
            }

            // Re-assign from fields if necessary.
            var activeService = (service == null) ? (dbName == OldDbName ? _oldPgService : _newPgService) : service;
            if (activeService == null) return;

            var effectiveName = string.IsNullOrWhiteSpace(targetDbName) ? dbName : targetDbName;
            
            // Get active connection details
            DatabaseConfig? targetConfig = null;
            if (cmbRestoreConnection.SelectedIndex == 2) {
                targetConfig = _customRestoreConfig;
            } else if (cmbRestoreConnection.SelectedIndex == 0) {
                targetConfig = _newDbConfig;
            } else {
                targetConfig = _oldDbConfig;
            }

            if (targetConfig != null) {
                AppendRestoreLog($"Starting restore into '{effectiveName}' on database server '{targetConfig.Host}:{targetConfig.Port}' (User: {targetConfig.Username}) from {filePath}...");
            } else {
                AppendRestoreLog($"Starting restore into '{effectiveName}' from {filePath}...");
            }
            // Keep button enabled so the user can click it to cancel the restore
            int errorCount = 0;
            int warningCount = 0;
            bool overallSuccess = false;

            try
            {
                var ext = Path.GetExtension(filePath).ToLower();
                // Thread-safe callback: marshal each line onto the UI thread
                Action<string> onOutput = line =>
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        var trimmed = line.Trim();
                        var matchCleanup = PsqlPathCleanupRegex.Match(trimmed);
                        if (matchCleanup.Success)
                        {
                            trimmed = $"[{matchCleanup.Groups[2].Value.ToUpper()}] Line {matchCleanup.Groups[1].Value}: {matchCleanup.Groups[3].Value}";
                            line = trimmed;
                        }
                        else
                        {
                            var matchFallback = PsqlFallbackCleanupRegex.Match(trimmed);
                            if (matchFallback.Success)
                            {
                                trimmed = $"Line {matchFallback.Groups[1].Value}: {matchFallback.Groups[2].Value}";
                                line = trimmed;
                            }
                        }

                        if (PsqlNoiseRegex.IsMatch(trimmed))
                        {
                            return;
                        }

                        if (trimmed.Contains("error:", StringComparison.OrdinalIgnoreCase) || 
                            trimmed.Contains("ERROR:", StringComparison.Ordinal) ||
                            trimmed.Contains("❌"))
                        {
                            errorCount++;
                        }
                        else if (trimmed.Contains("warning:", StringComparison.OrdinalIgnoreCase) || 
                                 trimmed.Contains("WARNING:", StringComparison.Ordinal) ||
                                 trimmed.Contains("⚠️"))
                        {
                            warningCount++;
                        }
                    }
                    AppendRestoreLog(line);
                };
                var opt = new RestoreOptions
                {
                    CleanBeforeRestore = chkCleanBefore.Checked,
                    SingleTransaction = chkSingleTransaction.Checked,
                    OnlySchema = chkOnlySchema.Checked,
                    OnlyData = chkOnlyData.Checked,
                    NoOwner = chkNoOwner.Checked,
                    NoPrivileges = chkNoPrivileges.Checked,
                    DisableTriggers = chkDisableTriggers.Checked,
                    NoTablespaces = chkNoTablespaces.Checked,
                    Verbose = chkVerboseRestore.Checked,
                    NumberOfJobs = (int)numRestoreJobs.Value,
                    Format = cmbRestoreFormat.SelectedItem?.ToString() ?? "Auto",
                    Section = cmbRestoreSection.SelectedItem?.ToString() ?? "All",
                    RoleName = txtRoleName.Text.Trim(),
                    IncludeCreateDb = chkIncludeCreateDb.Checked,
                    NoDataFailedTables = chkNoDataFailedTables.Checked,
                    ExitOnError = chkExitOnError.Checked,
                    UseSetSessionAuth = chkUseSetSessionAuth.Checked
                };

                if (pnlExtensions != null)
                {
                    foreach (Control ctrl in pnlExtensions.Controls)
                    {
                        if (ctrl is CheckBox chk && chk.Checked)
                        {
                            opt.ExtensionsToInstall.Add(chk.Text);
                        }
                    }
                }

                await activeService.RestoreDatabaseAsync(ext, filePath, string.IsNullOrWhiteSpace(targetDbName) ? null : targetDbName, opt, onOutput, _restoreCts?.Token ?? default);
                AppendRestoreLog($"✅ Restore into '{effectiveName}' completed successfully.");

                if (_postRestoreSqlFiles != null && _postRestoreSqlFiles.Count > 0)
                {
                    AppendRestoreLog($"⚡ Starting post-restore SQL scripts execution ({_postRestoreSqlFiles.Count} files)...");
                    foreach (var sqlFile in _postRestoreSqlFiles)
                    {
                        var fileName = Path.GetFileName(sqlFile);
                        AppendRestoreLog($"⌛ Running SQL script: {fileName} (Location: {sqlFile})...");
                        _sqlFileStatuses[sqlFile] = "running";
                        lstPostRestoreSqls.Invalidate();
                        try
                        {
                            await activeService.ExecuteScriptFileAsync(effectiveName, sqlFile, onOutput, _restoreCts?.Token ?? default);
                            _sqlFileStatuses[sqlFile] = "success";
                            lstPostRestoreSqls.Invalidate();
                            AppendRestoreLog($"✅ Successfully executed SQL script: {fileName}");
                        }
                        catch (Exception sqlEx)
                        {
                            _sqlFileStatuses[sqlFile] = "error";
                            lstPostRestoreSqls.Invalidate();
                            AppendRestoreLog($"❌ Error executing SQL script {fileName}: {sqlEx.Message}");
                            throw new Exception($"Post-restore script execution stopped at {fileName} due to error: {sqlEx.Message}");
                        }
                    }
                    AppendRestoreLog($"✅ All post-restore SQL scripts completed.");
                }
                overallSuccess = true;
            }
            catch (Exception ex)
            {
                errorCount++;
                AppendRestoreLog($"❌ Error during restore: {ex.Message}");
            }
            finally
            {
                AppendRestoreLog("==================================================================================");
                if (overallSuccess)
                {
                    if (errorCount == 0)
                    {
                        AppendRestoreLog($"🎉 SUMMARY: Restore into '{effectiveName}' completed successfully. Errors: 0, Warnings: {warningCount}");
                    }
                    else
                    {
                        AppendRestoreLog($"⚠️ SUMMARY: Restore into '{effectiveName}' completed with errors. Errors: {errorCount}, Warnings: {warningCount}");
                    }
                }
                else
                {
                    AppendRestoreLog($"❌ SUMMARY: Restore into '{effectiveName}' failed. Errors: {errorCount}, Warnings: {warningCount}");
                }
                AppendRestoreLog("==================================================================================");

                try
                {
                    Npgsql.NpgsqlConnection.ClearAllPools();
                }
                catch (Exception ex)
                {
                    AppendRestoreLog($"[WARNING] Failed to clear connection pools: {ex.Message}");
                }

                if (_restoreCts != null)
                {
                    _restoreCts.Dispose();
                    _restoreCts = null;
                }
                btnBackupOld.Text = "↻  Restore DB from File...";
                btnBackupOld.BackColor = UIConstants.Primary;
                btnBackupOld.Enabled = true;
            }
        }

        private void AppendRestoreLog(string line)
        {
            if (txtBackupLog == null) return;

            if (txtBackupLog.InvokeRequired)
            {
                txtBackupLog.Invoke(() => AppendRestoreLog(line));
                return;
            }

            if (line == null) return;
            var lines = line.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var l in lines)
            {
                _restoreLogLines.Add(l);
            }

            ApplyLogFilter();
        }

        private static string ShowInputDialog(string text, string caption, string defaultValue = "")
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 400;
                prompt.Height = 155;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = caption;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.BackColor = Color.White;

                Label textLabel = new Label() { Left = 20, Top = 15, Text = text, Width = 360, Font = new Font(UIConstants.MainFontName, 9f), ForeColor = UIConstants.TextPrimary };
                TextBox textBox = new TextBox() { Left = 20, Top = 42, Width = 360, Text = defaultValue, Font = new Font(UIConstants.MainFontName, 9.5f), BorderStyle = BorderStyle.FixedSingle };
                Button confirmation = new Button() { Text = "OK", Left = 190, Width = 85, Height = 28, Top = 80, DialogResult = DialogResult.OK, Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold) };
                Button cancel = new Button() { Text = "Cancel", Left = 285, Width = 85, Height = 28, Top = 80, DialogResult = DialogResult.Cancel, Font = new Font(UIConstants.MainFontName, 9f) };

                confirmation.BackColor = UIConstants.Primary;
                confirmation.ForeColor = Color.White;
                confirmation.FlatStyle = FlatStyle.Flat;
                confirmation.FlatAppearance.BorderSize = 0;

                cancel.BackColor = Color.White;
                cancel.ForeColor = UIConstants.TextPrimary;
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderColor = UIConstants.Border;

                prompt.Controls.Add(textBox);
                prompt.Controls.Add(textLabel);
                prompt.Controls.Add(confirmation);
                prompt.Controls.Add(cancel);
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;

                return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
            }
        }

        private void ApplyLogFilter()
        {
            if (txtBackupLog == null) return;

            if (txtBackupLog.InvokeRequired)
            {
                txtBackupLog.Invoke(ApplyLogFilter);
                return;
            }

            var filteredLines = new List<string>();
            string textFilter = txtLogFilter.Text.Trim();
            int filterType = cmbLogFilterType.SelectedIndex; // 0: All, 1: Errors, 2: Success, 3: Info/Cmd

            foreach (var line in _restoreLogLines)
            {
                bool matchesText = string.IsNullOrEmpty(textFilter) || line.IndexOf(textFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesType = true;

                if (filterType == 1) // Errors
                {
                    matchesType = !line.Contains("SUMMARY:") && !line.Contains("====") &&
                                  (line.Contains("❌") || 
                                   line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                   line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (filterType == 2) // Success
                {
                    matchesType = !line.Contains("SUMMARY:") && !line.Contains("====") &&
                                  (line.Contains("✅") || 
                                   line.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (filterType == 3) // Info/Cmd
                {
                    matchesType = !line.Contains("SUMMARY:") && !line.Contains("====") &&
                                  (line.Contains("⚡") || 
                                   line.Contains("⌛") || 
                                   line.IndexOf("[INFO]", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                   line.IndexOf("[CMD]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   line.IndexOf("pg_restore", StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (matchesText && matchesType)
                {
                    filteredLines.Add(line);
                }
            }

            txtBackupLog.Text = string.Join("\r\n", filteredLines) + (filteredLines.Count > 0 ? "\r\n" : "");
            txtBackupLog.SelectionStart = txtBackupLog.TextLength;
            txtBackupLog.ScrollToCaret();
        }

        private async Task RefreshRequiredExtensionsAsync()
        {
            if (pnlExtensions == null) return;
            
            pnlExtensions.Controls.Clear();
            if (txtRestoreFilePath.Tag is string filePath && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                var ext = Path.GetExtension(filePath).ToLower();
                var selectedService = cmbRestoreConnection.SelectedIndex == 2 ? _customRestorePgService : (cmbRestoreConnection.SelectedIndex == 0 ? _newPgService : _oldPgService);
                if (selectedService != null)
                {
                    try
                    {
                        var lblScanning = new Label {
                            Text = "Scanning backup file for required extensions...",
                            Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Italic),
                            ForeColor = Color.Gray,
                            AutoSize = true,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Margin = new Padding(0, 3, 0, 3)
                        };
                        pnlExtensions.Controls.Add(lblScanning);
                        
                        var requiredExts = await Task.Run(() => selectedService.GetRequiredExtensionsAsync(ext, filePath));
                        
                        pnlExtensions.Controls.Clear();
                        if (requiredExts != null && requiredExts.Count > 0)
                        {
                            foreach (var reqExt in requiredExts)
                            {
                                var chk = new CheckBox {
                                    Text = reqExt,
                                    Checked = true,
                                    AutoSize = true,
                                    Font = new Font(UIConstants.MainFontName, 8.5f),
                                    ForeColor = UIConstants.TextPrimary,
                                    Margin = new Padding(0, 3, 8, 3)
                                };
                                pnlExtensions.Controls.Add(chk);
                            }
                        }
                        else
                        {
                            var lblNone = new Label {
                                Text = "(None required)",
                                Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Italic),
                                ForeColor = Color.Gray,
                                AutoSize = true,
                                TextAlign = ContentAlignment.MiddleLeft,
                                Margin = new Padding(0, 3, 0, 3)
                            };
                            pnlExtensions.Controls.Add(lblNone);
                        }
                    }
                    catch (Exception ex)
                    {
                        pnlExtensions.Controls.Clear();
                        var lblError = new Label {
                            Text = $"(Scan error: {ex.Message})",
                            Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Italic),
                            ForeColor = Color.Red,
                            AutoSize = true,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Margin = new Padding(0, 3, 0, 3)
                        };
                        pnlExtensions.Controls.Add(lblError);
                    }
                }
            }
            else
            {
                var lblSelect = new Label {
                    Text = "(Select a file to scan extensions)",
                    Font = new Font(UIConstants.MainFontName, 8.5f, FontStyle.Italic),
                    ForeColor = Color.Gray,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 4, 0, 0)
                };
                pnlExtensions.Controls.Add(lblSelect);
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
                case "Enums": icon = UIConstants.IconSequence; iconColor = Color.FromArgb(153, 153, 0); break;
                case "Types": icon = UIConstants.IconExtension; iconColor = Color.FromArgb(0, 120, 212); break;
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
            _dbCompareService.IncludeOwner = chkIncludeOwner.Checked;
            _dbCompareService.IgnoreExtension = chkIgnoreExtension.Checked;
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
                var typeRoot = new TreeNode("Types");
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

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Type"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    typeRoot.Nodes.Add(node);
                }

                foreach (var diff in _schemaDiffs.Where(d => d.ObjectType == "Materialized View"))
                {
                    var node = new TreeNode(diff.ObjectName) { Tag = diff };
                    ApplyDiffColors(node, diff.DiffType);
                    matViewRoot.Nodes.Add(node);
                }

                tableRoot.Text = $"Tables ({tableRoot.Nodes.Count})";
                viewRoot.Text = $"Views ({viewRoot.Nodes.Count})";
                routineRoot.Text = $"Functions ({routineRoot.Nodes.Count})";
                indexNodeRoot.Text = $"Indexes ({indexNodeRoot.Nodes.Count})";
                triggerRoot.Text = $"Triggers ({triggerRoot.Nodes.Count})";
                constraintRoot.Text = $"Constraints ({constraintRoot.Nodes.Count})";
                extensionRoot.Text = $"Extensions ({extensionRoot.Nodes.Count})";
                roleRoot.Text = $"Roles ({roleRoot.Nodes.Count})";
                sequenceRoot.Text = $"Sequences ({sequenceRoot.Nodes.Count})";
                enumRoot.Text = $"Enums ({enumRoot.Nodes.Count})";
                typeRoot.Text = $"Types ({typeRoot.Nodes.Count})";
                matViewRoot.Text = $"Materialized Views ({matViewRoot.Nodes.Count})";

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
                if (typeRoot.Nodes.Count > 0) treeSchema.Nodes.Add(typeRoot);
                if (matViewRoot.Nodes.Count > 0) treeSchema.Nodes.Add(matViewRoot);
                
                tableRoot.Expand();

                pnlStatusLabels.Controls.Clear();
                if (_schemaDiffs.Count == 0)
                {
                    AddStatusBadge("✅ Identical (No Schema Diffs)", UIConstants.Success);
                }
                else
                {
                    AddStatusBadge($"{_schemaDiffs.Count} Differences", UIConstants.Primary);
                }
            }
            catch (Exception ex)
            {
                pnlStatusLabels.Controls.Clear();
                AddStatusBadge("Error", UIConstants.Danger);
                AppendRestoreLog($"Diff Error: {ex.Message}");
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
                    sb.AppendLine(r.DiffScript.Trim());
                    sb.AppendLine();
                }

                var sql = sb.ToString();
                if (chkTuningSchema.Checked)
                {
                    sql = SqlTuningHelper.TuneSchemaScript(sql, targetSchema);
                }
                
                var path = _fileSystemService!.GetSqlScriptPath(NewDbName, true);
                _fileSystemService!.WriteToFile(path, sql);
                _fileSystemService!.UpdateScriptUpdateNote();
                pnlStatusLabels.Controls.Clear();
                
                AddStatusBadge($"{UIConstants.IconRobot}  Review with AI", UIConstants.Primary, () => {
                    tabControl.SelectedIndex = 8; // Switch to AI Review tab
                });

                _lastSchemaExportPath = path;
                btnOpenSchemaFolder.Visible = true;
                btnEditSchema.Visible = true;

                MessageBox.Show($"Schema migration script generated successfully!\n\nSaved to: {path}\n\nYou can now review the script or proceed with AI Review.", "Generation Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    ReleasePath = txtReleasePath.Text, AiKey = txtAiKey.Text,
                    AiProvider = cmbAiProvider.SelectedItem?.ToString() ?? "Gemini",
                    AiModel = string.IsNullOrWhiteSpace(cmbAiModel.Text) ? "gemini-2.0-flash" : cmbAiModel.Text.Split(' ')[0],
                    ConfigCompareMode = rbCompareFolder.Checked ? "Folder" : "File",
                    ConfigCompareSource = txtOldConfigPath.Text,
                    ConfigCompareTarget = txtNewConfigPath.Text,
                    ConfigCompareSourceFile = _configCompareSourceFile,
                    ConfigCompareTargetFile = _configCompareTargetFile,
                    ConfigCompareSourceFolder = _configCompareSourceFolder,
                    ConfigCompareTargetFolder = _configCompareTargetFolder,
                    
                    // Tab 10 config
                    ConvertSourceFile = txtConvertSourceFile.Text,
                    ConvertTuning = chkConvertTuning.Checked,
                    IgnoreOwner = chkIgnoreOwner.Checked,
                    IgnorePrivileges = chkIgnorePrivileges.Checked,
                    IgnoreTablespaces = chkIgnoreTablespaces.Checked,
                    IgnoreComments = chkIgnoreComments.Checked,
                    IgnorePublications = chkIgnorePublications.Checked,
                    IgnoreSubscriptions = chkIgnoreSubscriptions.Checked,
                    IgnoreSecurityLabels = chkIgnoreSecurityLabels.Checked,
                    IgnoreTableAccessMethods = chkIgnoreTableAccessMethods.Checked,
                    IgnoreData = chkIgnoreData.Checked,
                    IgnoreSchema = chkIgnoreSchema.Checked,
                    IgnoreTransaction = chkIgnoreTransaction.Checked,
                    LastConvertedScriptPath = _lastConvertedScriptPath
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
                        
                        string? provider = (string?)config.AiProvider;
                        if (!string.IsNullOrEmpty(provider)) {
                            int idx = cmbAiProvider.FindStringExact(provider);
                            if (idx >= 0) cmbAiProvider.SelectedIndex = idx;
                        }
                        
                        txtAiKey.Text = (string?)config.AiKey ?? txtAiKey.Text;
                        if (string.IsNullOrWhiteSpace(txtAiKey.Text)) {
                            txtAiKey.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                        }
                        
                        string? model = (string?)config.AiModel;
                        if (!string.IsNullOrEmpty(model)) {
                            for (int i = 0; i < cmbAiModel.Items.Count; i++) {
                                if (cmbAiModel.Items[i]?.ToString()?.StartsWith(model) == true) {
                                    cmbAiModel.SelectedIndex = i;
                                    break;
                                }
                            }
                        }

                        UpdateConnectionLabels();

                        _configCompareSourceFile = (string?)config.ConfigCompareSourceFile ?? (string?)config.ConfigCompareSource ?? "";
                        _configCompareTargetFile = (string?)config.ConfigCompareTargetFile ?? (string?)config.ConfigCompareTarget ?? "";
                        _configCompareSourceFolder = (string?)config.ConfigCompareSourceFolder ?? (string?)config.ConfigCompareSource ?? "";
                        _configCompareTargetFolder = (string?)config.ConfigCompareTargetFolder ?? (string?)config.ConfigCompareTarget ?? "";

                        string? compMode = (string?)config.ConfigCompareMode;
                        _suppressConfigEvents = true;
                        try {
                            if (compMode == "Folder") {
                                rbCompareFolder.Checked = true;
                                txtOldConfigPath.Text = _configCompareSourceFolder;
                                txtNewConfigPath.Text = _configCompareTargetFolder;
                            } else {
                                rbCompareFile.Checked = true;
                                txtOldConfigPath.Text = _configCompareSourceFile;
                                txtNewConfigPath.Text = _configCompareTargetFile;
                            }
                        } finally {
                            _suppressConfigEvents = false;
                        }

                        // Tab 10 config loading
                        txtConvertSourceFile.Text = (string?)config.ConvertSourceFile ?? "";
                        chkConvertTuning.Checked = (bool?)config.ConvertTuning ?? false;
                        chkIgnoreOwner.Checked = (bool?)config.IgnoreOwner ?? false;
                        chkIgnorePrivileges.Checked = (bool?)config.IgnorePrivileges ?? false;
                        chkIgnoreTablespaces.Checked = (bool?)config.IgnoreTablespaces ?? false;
                        chkIgnoreComments.Checked = (bool?)config.IgnoreComments ?? false;
                        chkIgnorePublications.Checked = (bool?)config.IgnorePublications ?? false;
                        chkIgnoreSubscriptions.Checked = (bool?)config.IgnoreSubscriptions ?? false;
                        chkIgnoreSecurityLabels.Checked = (bool?)config.IgnoreSecurityLabels ?? false;
                        chkIgnoreTableAccessMethods.Checked = (bool?)config.IgnoreTableAccessMethods ?? false;
                        chkIgnoreData.Checked = (bool?)config.IgnoreData ?? false;
                        chkIgnoreSchema.Checked = (bool?)config.IgnoreSchema ?? false;
                        chkIgnoreTransaction.Checked = (bool?)config.IgnoreTransaction ?? false;
                        
                        _lastConvertedScriptPath = (string?)config.LastConvertedScriptPath ?? "";
                        if (!string.IsNullOrEmpty(_lastConvertedScriptPath) && File.Exists(_lastConvertedScriptPath)) {
                            btnOpenConvertFolder.Enabled = true;
                        }
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

                lblSourceDataDbTitle.Text = $"SOURCE DB ({_newDbConfig.Host}:{_newDbConfig.Port}):";
                lblTargetDataDbTitle.Text = $"TARGET DB ({_oldDbConfig.Host}:{_oldDbConfig.Port}):";

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

                // Update cmbJunkConnection items with host/port details
                cmbJunkConnection.Items.Clear();
                cmbJunkConnection.Items.Add($"Source (Dev) ({_newDbConfig.Host}:{_newDbConfig.Port})");
                cmbJunkConnection.Items.Add($"Target (Prod) ({_oldDbConfig.Host}:{_oldDbConfig.Port})");
                cmbJunkConnection.Items.Add("Custom Connection...");
                cmbJunkConnection.SelectedIndex = 1; // Default to Target (Prod)

                // Update cmbFinalExportConnection items with host/port details
                cmbFinalExportConnection.Items.Clear();
                cmbFinalExportConnection.Items.Add($"Source (Dev) ({_newDbConfig.Host}:{_newDbConfig.Port})");
                cmbFinalExportConnection.Items.Add($"Target (Prod) ({_oldDbConfig.Host}:{_oldDbConfig.Port})");
                cmbFinalExportConnection.Items.Add("Custom Connection...");
                cmbFinalExportConnection.SelectedIndex = 1; // Default to Target (Prod)

                // Load databases for final export
                await LoadFinalExportDbsAsync();

                // Load junk selection tree
                await UpdateJunkSelectionTreeAsync(true);
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
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge($"Load Error: {ex.Message}", UIConstants.Danger, null, pnlDataStatusLabels);
                lblDataStatus.Text = "❌ Error loading tables.";
            }
            finally
            {
                pbDataLoading.Visible = false;
                this.Cursor = Cursors.Default;
                Cursor.Current = Cursors.Default;
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
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge("Select tables first", UIConstants.Warning, null, pnlDataStatusLabels);
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

                    row.Cells["ColSource"].Value = summary.SourceRowCount;
                    row.Cells["ColTarget"].Value = summary.TargetRowCount;
                    row.Cells["ColDiff"].Value = $"+{summary.InsertedCount} / ~{summary.UpdatedCount} / -{summary.DeletedCount}";

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
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge($"Table Load Error: {ex.Message}", UIConstants.Danger, null, pnlDataStatusLabels);
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
                Cursor.Current = Cursors.Default;

                pnlDataStatusLabels.Controls.Clear();
                if (diffCount == 0)
                {
                    AddStatusBadge("✅ Identical (No Data Diffs)", UIConstants.Success, null, pnlDataStatusLabels);
                }
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
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge("Detail Load Error", UIConstants.Danger, null, pnlDataStatusLabels);
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                lblDataStatus.Text = "";
                this.Cursor = Cursors.Default;
                Cursor.Current = Cursors.Default;
            }
        }

        private async void BtnGenerateData_Click(object? sender, EventArgs e)
        {
            var tablesToSync = GetSelectedTables();
            if (!tablesToSync.Any()) { 
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge("Check tables first", UIConstants.Warning, null, pnlDataStatusLabels);
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
                if (chkTuningData.Checked)
                {
                    diffScript = SqlTuningHelper.TuneDataScript(diffScript, targetSchema);
                }
                
                var fullPath = _fileSystemService!.GetSqlScriptPath(NewDbName, false);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _fileSystemService!.WriteToFile(fullPath, diffScript);
                _fileSystemService!.UpdateScriptUpdateNote();
                _lastDataExportPath = fullPath;
                
                pnlDataStatusLabels.Controls.Clear();
                AddStatusBadge($"{UIConstants.IconRobot}  Review with AI", UIConstants.Primary, () => {
                    tabControl.SelectedIndex = 8; // Switch to AI Review tab
                }, pnlDataStatusLabels);

                btnOpenDataFolder.Visible = true;
                btnEditData.Visible = true;
                lblDataStatus.Text = $"✅ Exported to {Path.GetFileName(fullPath)}";

                MessageBox.Show($"Data synchronization script generated successfully!\n\nSaved to: {fullPath}\n\nYou can now review the script or proceed with AI Review.", "Generation Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating data sync script:\n{ex.Message}");
            }
            finally
            {
                this.Cursor = Cursors.Default;
                Cursor.Current = Cursors.Default;
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
            
            string targetDb = _oldDbConfig?.DatabaseName ?? "Unknown";
            string targetHost = _oldDbConfig?.Host ?? "localhost";
            int targetPort = _oldDbConfig?.Port ?? 5432;
            string fileName = Path.GetFileName(scriptPath);
            bool isDryRun = chkDryRun.Checked;
            
            try
            {
                txtExecuteLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] >>> Starting {type} synchronization...\r\n");
                txtExecuteLog.AppendText($"    Target Database : {targetDb} ({targetHost}:{targetPort})\r\n");
                txtExecuteLog.AppendText($"    Script File     : {fileName}\r\n");
                txtExecuteLog.AppendText($"    Transaction     : Started{(isDryRun ? " (Dry Run)" : "")}\r\n");
                
                var sql = File.ReadAllText(scriptPath);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                await _oldPgService!.ExecuteSqlWithTransactionAsync(sql, isDryRun);
                
                stopwatch.Stop();
                if (isDryRun)
                {
                    txtExecuteLog.AppendText($"    Transaction     : ROLLED BACK (Dry Run success)\r\n");
                    txtExecuteLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] <<< {type} sync dry-run completed successfully (Duration: {stopwatch.Elapsed.TotalSeconds:F2}s).\r\n\r\n");
                }
                else
                {
                    txtExecuteLog.AppendText($"    Transaction     : Committed successfully\r\n");
                    txtExecuteLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] <<< {type} sync executed successfully (Duration: {stopwatch.Elapsed.TotalSeconds:F2}s).\r\n\r\n");
                }
            }
            catch (Exception ex)
            {
                txtExecuteLog.AppendText($"    Transaction     : ROLLED BACK\r\n");
                txtExecuteLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ❌ Execution failed! Error: {ex.Message}\r\n\r\n");
            }
        }

        private PostgresService? GetActiveFinalExportPgService()
        {
             if (cmbFinalExportConnection.SelectedIndex == 0) return _newPgService;
             if (cmbFinalExportConnection.SelectedIndex == 1) return _oldPgService;
             if (cmbFinalExportConnection.SelectedIndex == 2 && _customFinalExportPgService != null) return _customFinalExportPgService;
             return _oldPgService;
        }

        private async Task LoadFinalExportDbsAsync()
        {
            var service = GetActiveFinalExportPgService();
            if (service == null) return;
            try
            {
                var dbs = await service.GetAllDatabasesAsync();
                clbFinalExportDbs.Items.Clear();
                var filteredDbs = dbs.Where(d => !d.StartsWith("pg_") && d != "postgres").OrderBy(d => d).ToArray();
                clbFinalExportDbs.Items.AddRange(filteredDbs);
                
                var defaultDb = cmbFinalExportConnection.SelectedIndex == 0 ? (_newDbConfig?.DatabaseName) : 
                                cmbFinalExportConnection.SelectedIndex == 1 ? (_oldDbConfig?.DatabaseName) : 
                                (_customFinalExportConfig?.DatabaseName);
                if (!string.IsNullOrEmpty(defaultDb))
                {
                    int index = clbFinalExportDbs.Items.IndexOf(defaultDb);
                    if (index >= 0)
                    {
                        clbFinalExportDbs.SetItemChecked(index, true);
                    }
                }
            }
            catch (Exception ex)
            {
                txtFinalExportLog.AppendText($"Error loading databases: {ex.Message}\r\n");
            }
        }

        private async void BtnExportFinal_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;
            var service = GetActiveFinalExportPgService();
            if (service == null)
            {
                MessageBox.Show("Please select a connection first.", "Connection Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var checkedItems = clbFinalExportDbs.CheckedItems;
            if (checkedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one database to export.", "Database Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                foreach (var item in checkedItems)
                {
                    string dbName = item.ToString()!;
                    txtFinalExportLog.AppendText($"Starting final export for {dbName}...\r\n");
                    var backupPath = _fileSystemService!.GetBackupPath(dbName);
                    await service.BackupDatabaseAsync(backupPath);
                    txtFinalExportLog.AppendText($"Backup saved to {backupPath}\r\n");

                    var fullPath = _fileSystemService!.GetFullScriptPath(dbName);
                    await service.DumpFullScriptAsync(fullPath);
                    txtFinalExportLog.AppendText($"Full script saved to {fullPath}\r\n");
                    txtFinalExportLog.AppendText($"--- Export completed for {dbName} ---\r\n\r\n");
                }
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

                // Check all nodes in structure tree by default
                tvJunkResults.BeginUpdate();
                foreach (TreeNode node in tvJunkResults.Nodes)
                {
                    node.Checked = true;
                    SetNodeCheckStateRecursive(node, true);
                }
                tvJunkResults.EndUpdate();
                
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

        private void DgvJunkDataResults_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == dgvJunkDataResults.Columns["Selected"]?.Index)
            {
                bool anyUnchecked = false;
                foreach (DataGridViewRow row in dgvJunkDataResults.Rows)
                {
                    if (row.Tag is JunkItem && !(row.Cells["Selected"].Value is bool b && b))
                    {
                        anyUnchecked = true;
                        break;
                    }
                }

                bool newCheckState = anyUnchecked;

                dgvJunkDataResults.SuspendLayout();
                try
                {
                    foreach (DataGridViewRow row in dgvJunkDataResults.Rows)
                    {
                        if (row.Tag is JunkItem || (row.Tag is string tag && tag.StartsWith("GROUP:")))
                        {
                            row.Cells["Selected"].Value = newCheckState;
                        }
                    }
                    dgvJunkDataResults.EndEdit();
                }
                finally
                {
                    dgvJunkDataResults.ResumeLayout();
                }
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

        private void SelectFolder(TextBox txt)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (Directory.Exists(txt.Text))
                {
                    fbd.SelectedPath = txt.Text;
                }
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txt.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnCompareConfig_Click(object? sender, EventArgs e)
        {
            if (!EnsureServicesInitialized()) return;

            if (rbCompareFolder.Checked)
            {
                if (!Directory.Exists(txtOldConfigPath.Text) || !Directory.Exists(txtNewConfigPath.Text))
                {
                    MessageBox.Show("Please select valid source and target configuration folders.", "Invalid Folders", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdateAiReadinessStatus();
                    return;
                }

                try
                {
                    var cmpService = new ConfigCompareService();
                    string diffOutput = cmpService.CompareDirectories(txtOldConfigPath.Text, txtNewConfigPath.Text, out bool hasChanges, out var cleanFiles);

                    var header = $"\r\n=== Configuration Version {txtReleaseVersion.Text} Update ===\r\n\r\n";
                    txtConfigDiffLog.Text = hasChanges ? (header + diffOutput) : diffOutput;

                    if (hasChanges && _fileSystemService != null)
                    {
                        var notePath = _fileSystemService.GetNoteFilePath();
                        _fileSystemService.WriteToFile(notePath, header + diffOutput);
                        
                        txtConfigDiffLog.AppendText($"\r\nNote generated at {notePath}");
                    }
                    else if (!hasChanges)
                    {
                        txtConfigDiffLog.Text = "No differences detected between the configuration folders.";
                    }
                }
                catch (Exception ex)
                {
                    txtConfigDiffLog.AppendText($"Error: {ex.Message}");
                }
                finally
                {
                    UpdateAiReadinessStatus();
                }
            }
            else
            {
                if (!File.Exists(txtOldConfigPath.Text) || !File.Exists(txtNewConfigPath.Text)) { UpdateAiReadinessStatus(); return; }

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

                    var header = $"\r\n=== Changes for {Path.GetFileName(txtNewConfigPath.Text)} ===\r\n\r\n";
                    txtConfigDiffLog.Text = hasChanges ? (header + diffOutput) : diffOutput;

                    if (hasChanges && _fileSystemService != null)
                    {
                        var notePath = _fileSystemService.GetNoteFilePath();
                        _fileSystemService.WriteToFile(notePath, header + diffOutput);
                        
                        txtConfigDiffLog.AppendText($"\r\nNote generated at {notePath}");
                    }
                    else if (!hasChanges)
                    {
                        txtConfigDiffLog.Text = "No differences detected between the configuration files.";
                    }
                }
                catch (Exception ex)
                {
                    txtConfigDiffLog.AppendText($"Error: {ex.Message}");
                }
                finally
                {
                    UpdateAiReadinessStatus();
                }
            }
        }

        private async void BtnReviewSchema_Click(object? sender, EventArgs e)
        {
            if (_isAiReviewRunning) return;
            if (!EnsureServicesInitialized() || _fileSystemService == null) return;
            UpdateAiReadinessStatus();
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, true);
            if (!File.Exists(path))
            {
                txtAiReviewLog.Text = "Schema script not generated yet.\r\nPlease generate schema sync script in Tab 3 first.";
                lblAiReviewStatus.Text = "Blocked: missing schema script";
                lblAiReviewStatus.ForeColor = Color.DarkRed;
                return;
            }

            if (_aiService == null) return;
            try
            {
                SetAiReviewUiState(true, "Running schema review...");
                txtAiReviewLog.Text = "Sending schema script to AI for review...\r\n";
                var result = await _aiService.ReviewSqlScriptAsync(File.ReadAllText(path), $"Release from {OldDbName} to {NewDbName}");
                txtAiReviewLog.Text = result;
                SetAiReviewUiState(false, "Schema review completed");
            }
            catch (Exception ex)
            {
                txtAiReviewLog.Text = $"AI schema review failed: {ex.Message}";
                SetAiReviewUiState(false, "Schema review failed", true);
            }
        }

        private async void BtnReviewData_Click(object? sender, EventArgs e)
        {
            if (_isAiReviewRunning) return;
            if (!EnsureServicesInitialized() || _fileSystemService == null) return;
            UpdateAiReadinessStatus();
            var path = _fileSystemService.GetSqlScriptPath(NewDbName, false);
            if (!File.Exists(path))
            {
                txtAiReviewLog.Text = "Data sync script not generated yet.\r\nPlease generate data sync script in Tab 4 first.";
                lblAiReviewStatus.Text = "Blocked: missing data script";
                lblAiReviewStatus.ForeColor = Color.DarkRed;
                return;
            }

            if (_aiService == null) return;
            try
            {
                SetAiReviewUiState(true, "Running data review...");
                txtAiReviewLog.Text = "Sending data sync script to AI for review...\r\n";
                var result = await _aiService.ReviewDataChangesAsync(File.ReadAllText(path), $"Release from {OldDbName} to {NewDbName}");
                txtAiReviewLog.Text = result;
                SetAiReviewUiState(false, "Data review completed");
            }
            catch (Exception ex)
            {
                txtAiReviewLog.Text = $"AI data review failed: {ex.Message}";
                SetAiReviewUiState(false, "Data review failed", true);
            }
        }

        private async void BtnReviewConfig_Click(object? sender, EventArgs e)
        {
            if (_isAiReviewRunning) return;
            if (!EnsureServicesInitialized() || _aiService == null) return;
            UpdateAiReadinessStatus();
            if (string.IsNullOrWhiteSpace(txtConfigDiffLog.Text))
            {
                txtAiReviewLog.Text = "Config diff is empty.\r\nPlease run config compare in Tab 8 first.";
                lblAiReviewStatus.Text = "Blocked: missing config diff";
                lblAiReviewStatus.ForeColor = Color.DarkRed;
                return;
            }
            
            try
            {
                SetAiReviewUiState(true, "Running config audit...");
                txtAiReviewLog.Text = "Sending configuration diff to AI for review...\r\n";
                var result = await _aiService.ReviewConfigChangesAsync(txtConfigDiffLog.Text);
                txtAiReviewLog.Text = result;
                SetAiReviewUiState(false, "Config audit completed");
            }
            catch (Exception ex)
            {
                txtAiReviewLog.Text = $"AI config audit failed: {ex.Message}";
                SetAiReviewUiState(false, "Config audit failed", true);
            }
        }

        private void UpdateAiReadinessStatus()
        {
            if (lblAiKeyReadiness == null || lblAiSchemaReadiness == null || lblAiDataReadiness == null || lblAiConfigReadiness == null)
                return;

            var hasAiKey = !string.IsNullOrWhiteSpace(txtAiKey?.Text);
            SetReadinessLabel(lblAiKeyReadiness, "AI Key", hasAiKey ? "Configured" : "Missing", hasAiKey);

            var hasSchemaScript = false;
            var hasDataScript = false;
            if (_fileSystemService != null)
            {
                try
                {
                    var schemaScriptPath = _fileSystemService.GetSqlScriptPath(NewDbName, true);
                    hasSchemaScript = File.Exists(schemaScriptPath);
                    var dataScriptPath = _fileSystemService.GetSqlScriptPath(NewDbName, false);
                    hasDataScript = File.Exists(dataScriptPath);
                }
                catch
                {
                    hasSchemaScript = false;
                    hasDataScript = false;
                }
            }
            SetReadinessLabel(lblAiSchemaReadiness, "Schema Script", hasSchemaScript ? "Ready" : "Missing", hasSchemaScript);
            SetReadinessLabel(lblAiDataReadiness, "Data Script", hasDataScript ? "Ready" : "Missing", hasDataScript);

            var configDiff = txtConfigDiffLog?.Text ?? "";
            if (string.IsNullOrWhiteSpace(configDiff) && _fileSystemService != null)
            {
                try
                {
                    var notePath = _fileSystemService.GetNoteFilePath();
                    if (File.Exists(notePath))
                    {
                        configDiff = File.ReadAllText(notePath);
                        if (txtConfigDiffLog != null)
                        {
                            txtConfigDiffLog.Text = configDiff;
                        }
                    }
                }
                catch { }
            }
            var hasConfigDiff = !string.IsNullOrWhiteSpace(configDiff) && !configDiff.TrimStart().StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
            SetReadinessLabel(lblAiConfigReadiness, "Config Diff", hasConfigDiff ? "Ready" : "Missing", hasConfigDiff);
        }

        private void SetReadinessLabel(Label label, string name, string value, bool isReady)
        {
            label.Text = $"{name}: {value}";
            label.ForeColor = isReady ? Color.DarkGreen : Color.DarkOrange;
        }

        private void SetAiReviewUiState(bool running, string status, bool isError = false)
        {
            _isAiReviewRunning = running;
            btnReviewSchema.Enabled = !running;
            btnReviewData.Enabled = !running;
            btnReviewConfig.Enabled = !running;

            btnReviewSchema.Text = running ? "⏳ Reviewing..." : "\u2728  Review Schema Changes";
            btnReviewData.Text = running ? "⏳ Reviewing..." : "\u2728  Review Data Changes";
            btnReviewConfig.Text = running ? "⏳ Auditing..." : "\u2728  Audit Configuration Diff";

            lblAiReviewStatus.Text = status;
            lblAiReviewStatus.ForeColor = isError ? Color.DarkRed : (running ? Color.DarkOrange : Color.DarkGreen);
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
        private void AddStatusBadge(string text, Color color, Action? onClick = null, FlowLayoutPanel? targetPanel = null)
        {
            var panel = targetPanel ?? pnlStatusLabels;
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

            panel.Controls.Add(lbl);
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
