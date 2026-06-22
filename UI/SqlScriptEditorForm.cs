using System;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace ReleasePrepTool.UI
{
    public partial class SqlScriptEditorForm : Form
    {
        private RichTextBox _rtbSql = default!;
        private Panel _pnlGutter = default!;
        private Label _lblLineCount = default!;
        public string EditedScript { get; private set; } = "";
        private string _title;
        private string _actionText;
        private TextBox _txtSearch = default!;
        private Label _lblSearchStatus = default!;
        private string _initialScript;
        private Panel _pnlLoading = default!;
        private ProgressBar _pbLoading = default!;
        private List<(int start, int len, Color color, bool bold)> _allMatches = new();
        private HashSet<int> _highlightedLines = new();
        private int _lineHeight;
        private Font _fontGutter = default!;
        private Brush _brushGutter = default!;

        // Search state
        private List<int> _searchMatches = new();
        private int _searchCurrentIndex = -1;
        private string _lastSearchQuery = "";

        private class GutterPanel : Panel {
            public GutterPanel() {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, ref Win32Point lParam);
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_VSCROLL = 0x115;
        private const int EM_GETSCROLLPOS = 0x0400 + 221;
        private const int EM_SETSCROLLPOS = 0x0400 + 222;
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_SETEVENTMASK = 0x0400 + 69;

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Point { public int X; public int Y; }

        public SqlScriptEditorForm(string initialScript, string title = "SQL Script Review", string actionText = "Save & Apply")
        {
            _title = title;
            _actionText = actionText;
            _initialScript = initialScript;
            
            InitializeComponent();
            
            this.Shown += async (s, e) => {
                _pnlLoading.Visible = true;
                _pnlLoading.BringToFront();
                _pbLoading.Value = 0;
                
                await System.Threading.Tasks.Task.Delay(300);
                
                SendMessage(_rtbSql.Handle, WM_SETREDRAW, 0, 0);
                _rtbSql.Text = _initialScript;
                _rtbSql.SelectAll();
                _rtbSql.SelectionColor = Color.FromArgb(212, 212, 212);
                _rtbSql.DeselectAll();
                SendMessage(_rtbSql.Handle, WM_SETREDRAW, 1, 0);
                _rtbSql.Invalidate();
                
                // Phase 1: Background Matching (0 -> 30%)
                await CalculateAllMatchesAsync();
                
                // Phase 2: Full Document Highlighting (30 -> 100%)
                await ApplyFullHighlightingAsync();
                
                _pnlLoading.Visible = false;
            };
        }

        private void InitializeComponent()
        {
            this.Text = _title;
            this.Size = new Size(1150, 850);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Icon = SystemIcons.Application;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.WindowState = FormWindowState.Maximized;
            this.KeyPreview = true;

            // Header: Modern Dark InfoBar Style
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(45, 45, 48) };
            pnlTop.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(60, 60, 60), 1), 0, pnlTop.Height - 1, pnlTop.Width, pnlTop.Height - 1);
            };

            var lblIcon = new Label { 
                Text = UIConstants.IconInfo,
                Font = new Font(UIConstants.IconFontName, 11f),
                ForeColor = Color.FromArgb(0, 150, 255),
                Location = new Point(18, 18),
                AutoSize = true
            };

            var lblHint = new Label { 
                Text = "Review and modify the generated SQL script. Changes will be saved to the local file upon clicking '" + _actionText + "'.", 
                Location = new Point(46, 0),
                Height = 56,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Font = new Font(UIConstants.MainFontName, 9.5f),
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            pnlTop.Controls.Add(lblIcon);
            pnlTop.Controls.Add(lblHint);
            lblIcon.Top = (pnlTop.Height - lblIcon.Height) / 2;
            lblHint.Top = (pnlTop.Height - lblHint.Height) / 2;

            var flowSearch = new FlowLayoutPanel { 
                Dock = DockStyle.Right, 
                Width = 480, 
                FlowDirection = FlowDirection.LeftToRight, 
                Padding = new Padding(0, 12, 20, 0),
                BackColor = Color.Transparent
            };
            
            var lblSearchIcon = new Label { Text = UIConstants.IconSearch, Font = new Font(UIConstants.IconFontName, 10f), ForeColor = Color.Gray, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            
            _txtSearch = new TextBox { Width = 280, Height = 28, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Search in script..." };
            _txtSearch.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) { FindNext(true); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { ToggleSearch(false); }
            };

            var btnNext = CreateHeaderIconButton("\uE70D", "Find Next", (s, e) => FindNext(true));
            var btnPrev = CreateHeaderIconButton("\uE70E", "Find Previous", (s, e) => FindNext(false));

            flowSearch.Controls.AddRange(new Control[] { lblSearchIcon, _txtSearch, btnNext, btnPrev });
            pnlTop.Controls.Add(flowSearch);

            // 1. Status Bar (Bottom-most)
            var pnlStatusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(0, 122, 204) };
            _lblLineCount = new Label { Text = "Total: 0 lines", ForeColor = Color.White, Font = new Font(UIConstants.MainFontName, 8.5f), Dock = DockStyle.Left, Width = 150, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
            _lblSearchStatus = new Label { Text = "", ForeColor = Color.White, Font = new Font(UIConstants.MainFontName, 8.5f), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 12, 0) };
            pnlStatusBar.Controls.Add(_lblSearchStatus);
            pnlStatusBar.Controls.Add(_lblLineCount);

            // 2. Action Panel (Above Status Bar)
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(37, 37, 38) };
            pnlBottom.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(60, 60, 60), 1), 0, 0, pnlBottom.Width, 0);
            };

            var tblButtons = new TableLayoutPanel { 
                Dock = DockStyle.Right, 
                Width = 570, 
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 14, 20, 0)
            };
            tblButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tblButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            tblButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            tblButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200f));

            var btnCancel = new Button { 
                Text = UIConstants.IconClear + "  Cancel", 
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font(UIConstants.MainFontName, 9f),
                BackColor = Color.FromArgb(75, 75, 75),
                Margin = new Padding(5)
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.MouseEnter += (s, e) => btnCancel.BackColor = Color.FromArgb(95, 95, 95);
            btnCancel.MouseLeave += (s, e) => btnCancel.BackColor = Color.FromArgb(75, 75, 75);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            var btnCopy = new Button { 
                Text = UIConstants.IconCopy + "  Copy", 
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font(UIConstants.MainFontName, 9f),
                BackColor = Color.FromArgb(75, 75, 75),
                Margin = new Padding(5)
            };
            btnCopy.FlatAppearance.BorderSize = 0;
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.MouseEnter += (s, e) => btnCopy.BackColor = Color.FromArgb(95, 95, 95);
            btnCopy.MouseLeave += (s, e) => btnCopy.BackColor = Color.FromArgb(75, 75, 75);
            btnCopy.Click += (s, e) => {
                if (!string.IsNullOrEmpty(_rtbSql.Text)) {
                    Clipboard.SetText(_rtbSql.Text);
                    MessageBox.Show("SQL Script has been copied to Clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var btnSaveAs = new Button { 
                Text = UIConstants.IconExport + "  Save As...", 
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font(UIConstants.MainFontName, 9f),
                BackColor = Color.FromArgb(75, 75, 75),
                Margin = new Padding(5)
            };
            btnSaveAs.FlatAppearance.BorderSize = 0;
            btnSaveAs.Cursor = Cursors.Hand;
            btnSaveAs.MouseEnter += (s, e) => btnSaveAs.BackColor = Color.FromArgb(95, 95, 95);
            btnSaveAs.MouseLeave += (s, e) => btnSaveAs.BackColor = Color.FromArgb(75, 75, 75);

            btnSaveAs.Click += (s, e) => {
                using (var sfd = new SaveFileDialog { Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*", Title = "Save SQL Script As" }) {
                    if (sfd.ShowDialog() == DialogResult.OK) {
                        File.WriteAllText(sfd.FileName, _rtbSql.Text);
                        MessageBox.Show($"File saved successfully to:\n{sfd.FileName}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };

            var btnApply = new Button { 
                Text = UIConstants.IconCheck + "  " + _actionText, 
                Dock = DockStyle.Fill,
                BackColor = UIConstants.Primary, 
                ForeColor = Color.White, 
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(5)
            };
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Cursor = Cursors.Hand;
            btnApply.MouseEnter += (s, e) => btnApply.BackColor = UIConstants.PrimaryHover;
            btnApply.MouseLeave += (s, e) => btnApply.BackColor = UIConstants.Primary;
            btnApply.Click += (s, e) => {
                this.EditedScript = _rtbSql.Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            tblButtons.Controls.Add(btnCancel, 0, 0);
            tblButtons.Controls.Add(btnCopy, 1, 0);
            tblButtons.Controls.Add(btnSaveAs, 2, 0);
            tblButtons.Controls.Add(btnApply, 3, 0);
            pnlBottom.Controls.Add(tblButtons);

            // Main Editor Container
            var pnlEditor = new Panel { Dock = DockStyle.Fill };
            pnlEditor.BackColor = Color.FromArgb(30, 30, 30);
            
            _fontGutter = new Font("Consolas", 11f);
            _brushGutter = new SolidBrush(Color.FromArgb(140, 140, 140));
            
            // Pre-calculate line height for the current font
            using (var g = this.CreateGraphics()) {
                _lineHeight = (int)Math.Ceiling(g.MeasureString("123", _fontGutter).Height);
            }
            // Small calibration for RichTextBox line spacing
            _lineHeight = Math.Max(_lineHeight, 18); 

            _pnlGutter = new GutterPanel {
                Width = 55,
                Dock = DockStyle.Left,
                BackColor = Color.FromArgb(37, 37, 38),
            };
            _pnlGutter.Paint += (s, e) => DrawLineNumbers(e.Graphics);

            _rtbSql = new RichTextBox {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11f),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(212, 212, 212),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                AcceptsTab = true,
                HideSelection = false
            };

            // Scroll Synchronization
            _rtbSql.VScroll += (s, e) => _pnlGutter.Invalidate();
            _rtbSql.SizeChanged += (s, e) => _pnlGutter.Invalidate();
            _rtbSql.TextChanged += (s, e) => _pnlGutter.Invalidate();

            pnlEditor.Controls.Add(_rtbSql);
            pnlEditor.Controls.Add(_pnlGutter);

            // Loading Overlay
            _pnlLoading = new Panel { 
                Dock = DockStyle.Fill, 
                BackColor = Color.FromArgb(180, 20, 20, 20), // Dark semi-transparent
                Visible = false 
            };
            var lblLoading = new Label {
                Text = UIConstants.IconTimer + "  Processing SQL Script...",
                ForeColor = Color.White,
                Font = new Font(UIConstants.MainFontName, 11f, FontStyle.Bold),
                AutoSize = false,
                Width = 400,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            _pbLoading = new ProgressBar {
                Width = 300,
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                ForeColor = UIConstants.Primary,
                BackColor = Color.FromArgb(60, 60, 60)
            };

            // Layout Loading Components
            lblLoading.Location = new Point((_pnlLoading.Width - lblLoading.Width) / 2, (_pnlLoading.Height / 2) - 40);
            _pbLoading.Location = new Point((_pnlLoading.Width - _pbLoading.Width) / 2, (_pnlLoading.Height / 2) + 5);
            
            _pnlLoading.SizeChanged += (s, e) => {
                lblLoading.Location = new Point((_pnlLoading.Width - lblLoading.Width) / 2, (_pnlLoading.Height / 2) - 40);
                _pbLoading.Location = new Point((_pnlLoading.Width - _pbLoading.Width) / 2, (_pnlLoading.Height / 2) + 5);
            };

            _pnlLoading.Controls.Add(lblLoading);
            _pnlLoading.Controls.Add(_pbLoading);
            pnlEditor.Controls.Add(_pnlLoading);

            this.Controls.Add(pnlEditor);
            this.Controls.Add(pnlTop);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlStatusBar);

            this.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.S) {
                    btnApply.PerformClick();
                    e.SuppressKeyPress = true;
                }
                if (e.Control && e.KeyCode == Keys.F) {
                    _txtSearch.Focus();
                    _txtSearch.SelectAll();
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void ToggleSearch(bool show) {
            if (show) _txtSearch.Focus();
            else _rtbSql.Focus();
        }

        private void ClearSearchHighlights() {
            if (_searchMatches.Count == 0) return;
            // Restore background only for the current highlighted match
            if (_searchCurrentIndex >= 0 && _searchCurrentIndex < _searchMatches.Count) {
                int pos = _searchMatches[_searchCurrentIndex];
                SendMessage(_rtbSql.Handle, WM_SETREDRAW, 0, 0);
                int savedStart = _rtbSql.SelectionStart;
                int savedLen   = _rtbSql.SelectionLength;
                try {
                    _rtbSql.Select(pos, _lastSearchQuery.Length);
                    _rtbSql.SelectionBackColor = Color.FromArgb(30, 30, 30);
                    _rtbSql.SelectionColor = Color.FromArgb(212, 212, 212);
                } finally {
                    SendMessage(_rtbSql.Handle, WM_SETREDRAW, 1, 0);
                    _rtbSql.Select(savedStart, savedLen);
                    _rtbSql.Invalidate();
                }
            }
            _searchMatches.Clear();
            _searchCurrentIndex = -1;
        }

        private void ApplySearchHighlights(string query) {
            _searchMatches.Clear();
            if (string.IsNullOrEmpty(query)) {
                _lblSearchStatus.Text = "";
                return;
            }

            // Just collect positions, do NOT change any colors here
            string text = _rtbSql.Text;
            int idx = 0;
            while (true) {
                idx = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                _searchMatches.Add(idx);
                idx += query.Length;
            }

            if (_searchMatches.Count == 0) {
                _lblSearchStatus.Text = "No matches";
            }
        }

        private void ScrollToMatch(int matchIndex) {
            if (matchIndex < 0 || matchIndex >= _searchMatches.Count) return;
            string query = _lastSearchQuery;

            SendMessage(_rtbSql.Handle, WM_SETREDRAW, 0, 0);
            try {
                // Clear previous highlight
                if (_searchCurrentIndex >= 0 && _searchCurrentIndex < _searchMatches.Count) {
                    int prevPos = _searchMatches[_searchCurrentIndex];
                    _rtbSql.Select(prevPos, query.Length);
                    _rtbSql.SelectionBackColor = Color.FromArgb(30, 30, 30);
                    _rtbSql.SelectionColor = Color.FromArgb(212, 212, 212);
                }

                // Highlight only current match
                int pos = _searchMatches[matchIndex];
                _rtbSql.Select(pos, query.Length);
                _rtbSql.SelectionBackColor = Color.FromArgb(255, 200, 0);
                _rtbSql.SelectionColor = Color.Black;
            } finally {
                SendMessage(_rtbSql.Handle, WM_SETREDRAW, 1, 0);
                _rtbSql.Invalidate();
            }

            // Move caret and scroll into view
            int curPos = _searchMatches[matchIndex];
            _rtbSql.Select(curPos, query.Length);
            _rtbSql.ScrollToCaret();

            _searchCurrentIndex = matchIndex;
            _lblSearchStatus.Text = $"{UIConstants.IconCheck}  {matchIndex + 1} / {_searchMatches.Count} matches";
        }

        private void FindNext(bool forward) {
            string query = _txtSearch.Text;
            if (string.IsNullOrEmpty(query)) {
                ClearSearchHighlights();
                _lblSearchStatus.Text = "";
                return;
            }

            // Re-index if query changed
            if (query != _lastSearchQuery) {
                ClearSearchHighlights();
                _lastSearchQuery = query;
                _searchCurrentIndex = -1;
                ApplySearchHighlights(query);
            }

            if (_searchMatches.Count == 0) return;

            int nextIndex;
            if (forward) {
                nextIndex = (_searchCurrentIndex + 1) % _searchMatches.Count;
            } else {
                nextIndex = (_searchCurrentIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            }

            ScrollToMatch(nextIndex);
        }

        private Button CreateHeaderIconButton(string icon, string tooltip, EventHandler click) {
            var btn = new Button { Text = icon, Font = new Font(UIConstants.IconFontName, 10f), Width = 34, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.Silver, BackColor = Color.Transparent, Margin = new Padding(4, 0, 0, 0) };
            btn.FlatAppearance.BorderSize = 0; btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70); btn.Cursor = Cursors.Hand; btn.Click += click;
            new ToolTip().SetToolTip(btn, tooltip);
            return btn;
        }

        private void DrawLineNumbers(Graphics g)
        {
            if (_rtbSql == null || string.IsNullOrEmpty(_rtbSql.Text) || this.IsDisposed) return;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // 1. Get ONLY the first visible character/line info (Slow part - called once)
            int firstChar = _rtbSql.GetCharIndexFromPosition(new Point(0, 0));
            int firstLine = _rtbSql.GetLineFromCharIndex(firstChar);
            Point firstLinePos = _rtbSql.GetPositionFromCharIndex(firstChar);

            // 2. Iterate and draw using simple arithmetic (Fast part)
            int currentY = firstLinePos.Y;
            int totalLines = _rtbSql.Lines.Length;

            for (int i = firstLine; i < totalLines; i++)
            {
                if (currentY > _pnlGutter.Height) break;

                string lineNum = (i + 1).ToString();
                SizeF size = g.MeasureString(lineNum, _fontGutter);
                
                // Draw line number right-aligned
                g.DrawString(lineNum, _fontGutter, _brushGutter, _pnlGutter.Width - size.Width - 8, currentY);
                
                currentY += _lineHeight;
            }
        }

        private void SyncGutterScroll() => _pnlGutter.Invalidate();
        private void UpdateLineNumbers() => _pnlGutter.Invalidate();

        private async System.Threading.Tasks.Task CalculateAllMatchesAsync()
        {
            if (string.IsNullOrEmpty(_rtbSql.Text)) return;
            string text = _rtbSql.Text;

            _pnlLoading.Visible = true;
            _pbLoading.Value = 10;

            _allMatches = await System.Threading.Tasks.Task.Run(() => {
                var list = new List<(int start, int len, Color color, bool bold)>();
                list.AddRange(GetMatches(text, @"\b(SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|DROP|SCHEMA|TABLE|VIEW|PROCEDURE|FUNCTION|ROUTINE|IF|EXISTS|CASCADE|RESTRICT|JOIN|ON|IN|NOT|NULL|AND|OR|ILIKE|LIKE|LIMIT|GROUP|BY|ORDER|BY|CREATE|DATABASE|SET|USE|ALTER|ADD|COLUMN|CONSTRAINT|PRIMARY|KEY|UNIQUE|INDEX|CHECK|TRUNCATE|GRANT|REVOKE|COMMIT|ROLLBACK|BEGIN|TRANSACTION|SEQUENCE|DOMAIN|TYPE|EXTENSION|ROLE|USER|GRANT|TO|WITH|PASSWORD|OWNER|DEFAULT|FOR|EACH|ROW|EXECUTE|FUNCTION|TRIGGER|BEFORE|AFTER|INSTEAD|OF|RETURNS|LANGUAGE|VOLATILE|IMMUTABLE|STABLE)\b", Color.FromArgb(86, 156, 214), true));
                list.AddRange(GetMatches(text, @"'[^']*'", Color.FromArgb(206, 145, 120)));
                list.AddRange(GetMatches(text, @"(--.*$)|(/\*[\s\S]*?\*/)", Color.FromArgb(106, 153, 85), false, true));
                list.AddRange(GetMatches(text, @"""[^""]+""", Color.FromArgb(78, 201, 176)));
                list.AddRange(GetMatches(text, @"\b\d+\b", Color.FromArgb(181, 206, 168)));
                list.AddRange(GetMatches(text, @"[\=,\(\);]", Color.FromArgb(160, 160, 160)));
                return list;
            });

            _pbLoading.Value = 30;
        }

        private async System.Threading.Tasks.Task ApplyFullHighlightingAsync()
        {
            if (_allMatches == null || _allMatches.Count == 0 || this.IsDisposed) {
                _pbLoading.Value = 100;
                return;
            }

            int total = _allMatches.Count;
            int chunkSize = 400;
            int selStart = _rtbSql.SelectionStart;
            int selLen = _rtbSql.SelectionLength;

            for (int i = 0; i < total; i += chunkSize)
            {
                if (this.IsDisposed) return;

                // Progress: 30% -> 100%
                int progress = 30 + (int)((double)i / total * 70);
                if (_pbLoading != null) _pbLoading.Value = Math.Min(progress, 100);

                SendMessage(_rtbSql.Handle, WM_SETREDRAW, 0, 0);
                int eventMask = SendMessage(_rtbSql.Handle, EM_SETEVENTMASK, 0, 0);

                try {
                    int end = Math.Min(i + chunkSize, total);
                    for (int j = i; j < end; j++)
                    {
                        var t = _allMatches[j];
                        _rtbSql.Select(t.start, t.len);
                        _rtbSql.SelectionColor = t.color;
                        if (t.bold && _rtbSql.SelectionFont != null)
                            _rtbSql.SelectionFont = new Font(_rtbSql.Font, FontStyle.Bold);
                    }
                } finally {
                    SendMessage(_rtbSql.Handle, EM_SETEVENTMASK, 0, eventMask);
                    SendMessage(_rtbSql.Handle, WM_SETREDRAW, 1, 0);
                    _rtbSql.Invalidate(new Rectangle(0, 0, 1, 1));
                }

                _rtbSql.Select(selStart, selLen);
                
                // More frequent yields during large full-document passes to keep bar moving
                await System.Threading.Tasks.Task.Yield();
            }

            if (_pbLoading != null) _pbLoading.Value = 100;
            _rtbSql.Invalidate();
        }

        private List<(int start, int len, Color color, bool bold)> GetMatches(string text, string pattern, Color color, bool isBold = false, bool isMultiline = false)
        {
            var matches = new List<(int start, int len, Color color, bool bold)>();
            var options = RegexOptions.IgnoreCase;
            if (isMultiline) options |= RegexOptions.Multiline;

            foreach (Match match in Regex.Matches(text, pattern, options))
                matches.Add((match.Index, match.Length, color, isBold));
            
            return matches;
        }
    }
}
