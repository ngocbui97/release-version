using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ReleasePrepTool.UI
{
    public class SqlReviewDialog : Form
    {
        private RichTextBox txtSourceDdl = null!;
        private RichTextBox txtTargetDdl = null!;
        private RichTextBox txtSourceLineNumbers = null!;
        private RichTextBox txtTargetLineNumbers = null!;
        
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, ref Point lParam);
        
        private const int WM_VSCROLL = 0x0115;
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETSCROLLPOS = 0x0400 + 221;
        private const int EM_SETSCROLLPOS = 0x0400 + 222;

        private string _summaryText = "";

        public SqlReviewDialog(string originalSql, string convertedSql, string summaryText = "")
        {
            _summaryText = summaryText;
            InitializeComponent();
            LoadDiff(originalSql, convertedSql);
        }

        private void InitializeComponent()
        {
            this.Text = "SQL Script Conversion Review";
            this.Size = new Size(1300, 800);
            this.StartPosition = FormStartPosition.CenterParent;
            this.WindowState = FormWindowState.Maximized;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.Icon = SystemIcons.Application;
            this.BackColor = Color.White;

            // Header Panel
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(245, 247, 250) };
            pnlHeader.Paint += (s, e) => {
                using var pen = new Pen(Color.FromArgb(218, 224, 233), 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            // Right side container (docked to Right)
            var pnlHeaderRight = new Panel {
                Dock = DockStyle.Right,
                Width = 600,
                BackColor = Color.Transparent
            };

            // Left side container (docked to Fill)
            var pnlHeaderLeft = new Panel {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            string summaryDisplay = "📊  Total Statements: --  |  Unchanged: --  |  Modified: --  |  Ignored/Removed: --";
            if (!string.IsNullOrEmpty(_summaryText))
            {
                var matchTotal = Regex.Match(_summaryText, @"Total original statements:\s*(\d+)");
                var matchUnchanged = Regex.Match(_summaryText, @"Unchanged statements:\s*(\d+)");
                var matchModified = Regex.Match(_summaryText, @"Modified statements:\s*(\d+)");
                var matchIgnored = Regex.Match(_summaryText, @"Ignored \(removed\) statements:\s*(\d+)");

                if (matchTotal.Success && matchUnchanged.Success && matchModified.Success && matchIgnored.Success)
                {
                    summaryDisplay = $"📊  Total Statements: {matchTotal.Groups[1].Value}   •   Unchanged: {matchUnchanged.Groups[1].Value}   •   Modified: {matchModified.Groups[1].Value}   •   Removed: {matchIgnored.Groups[1].Value}";
                }
            }

            var lblSummary = new Label {
                Text = summaryDisplay,
                Font = new Font(UIConstants.MainFontName, 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 110, 125),
                Location = new Point(15, 14),
                Size = new Size(600, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlHeaderLeft.Controls.Add(lblSummary);

            var btnClose = new Button {
                Text = "Close Preview",
                Size = new Size(130, 28),
                Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold),
                Location = new Point(600 - 130 - 15, 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = UIConstants.Primary,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();

            // Legend FlowLayout
            var pnlLegend = new FlowLayoutPanel {
                FlowDirection = FlowDirection.LeftToRight,
                Location = new Point(600 - 420 - 130 - 20, 12),
                Size = new Size(420, 24),
                BackColor = Color.Transparent
            };

            Control CreateLegendItem(string text, Color color) {
                var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(15, 0, 0, 0) };
                var box = new Panel { Width = 12, Height = 12, BackColor = color, Margin = new Padding(0, 2, 6, 0), BorderStyle = BorderStyle.FixedSingle };
                var lbl = new Label { Text = text, Font = new Font(UIConstants.MainFontName, 8.5f), AutoSize = true };
                p.Controls.AddRange(new Control[] { box, lbl });
                return p;
            }

            pnlLegend.Controls.Add(CreateLegendItem("Removed", Color.FromArgb(255, 230, 230)));
            pnlLegend.Controls.Add(CreateLegendItem("Added/Tuned", Color.FromArgb(232, 252, 232)));
            pnlLegend.Controls.Add(CreateLegendItem("Modified", Color.FromArgb(230, 240, 255)));

            pnlHeaderRight.Controls.Add(btnClose);
            pnlHeaderRight.Controls.Add(pnlLegend);

            pnlHeader.Controls.Add(pnlHeaderLeft);
            pnlHeader.Controls.Add(pnlHeaderRight);

            // Main Code container Split Panel
            var pnlDdl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(10) };
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

            // Sync scroll
            txtSourceDdl.VScroll += (s, e) => SyncScroll(txtSourceDdl, txtTargetDdl);
            txtTargetDdl.VScroll += (s, e) => SyncScroll(txtTargetDdl, txtSourceDdl);

            // Add to Controls
            this.Controls.Add(pnlDdl);
            this.Controls.Add(pnlHeader);
        }

        private bool _isSyncingScroll = false;
        private void SyncScroll(RichTextBox source, RichTextBox target)
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            try
            {
                Point scrollPos = new Point();
                SendMessage(source.Handle, EM_GETSCROLLPOS, 0, ref scrollPos);
                SendMessage(target.Handle, EM_SETSCROLLPOS, 0, ref scrollPos);

                // Sync the corresponding gutters
                SyncGutterScroll(txtSourceDdl, txtSourceLineNumbers);
                SyncGutterScroll(txtTargetDdl, txtTargetLineNumbers);
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }

        private void SyncGutterScroll(RichTextBox source, RichTextBox gutter)
        {
            int charIndex = source.GetCharIndexFromPosition(new Point(0, 0));
            int lineIndex = source.GetLineFromCharIndex(charIndex);

            int gutterCharIndex = gutter.GetFirstCharIndexFromLine(lineIndex);
            if (gutterCharIndex < 0) return;

            gutter.Select(gutterCharIndex, 0);
            gutter.ScrollToCaret();
        }

        private void LoadDiff(string source, string target)
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

            // Compute LCS for line alignment
            int[,] lcs = new int[m + 1, n + 1];

            for (int r = 1; r <= m; r++)
            {
                for (int c = 1; c <= n; c++)
                {
                    if (sLines[r - 1].Trim() == tLines[c - 1].Trim() || IsLikelyChange(sLines[r - 1], tLines[c - 1]))
                        lcs[r, c] = lcs[r - 1, c - 1] + 1;
                    else
                        lcs[r, c] = Math.Max(lcs[r - 1, c], lcs[r, c - 1]);
                }
            }

            int i = m, j = n;
            while (i > 0 || j > 0)
            {
                if (i > 0 && j > 0 && (sLines[i - 1].Trim() == tLines[j - 1].Trim() || IsLikelyChange(sLines[i - 1], tLines[j - 1])))
                {
                    if (sLines[i - 1].Trim() == tLines[j - 1].Trim())
                    {
                        diffRows.Add((sLines[i - 1], tLines[j - 1], Color.White, Color.White, false));
                    }
                    else
                    {
                        var modCol = Color.FromArgb(230, 240, 255);
                        diffRows.Add((sLines[i - 1], tLines[j - 1], modCol, modCol, true));
                    }
                    i--; j--;
                }
                else if (i > 0 && (j == 0 || lcs[i - 1, j] >= lcs[i, j - 1]))
                {
                    diffRows.Add((sLines[i - 1], null, Color.FromArgb(255, 230, 230), Color.White, false));
                    i--;
                }
                else
                {
                    diffRows.Add((null, tLines[j - 1], Color.White, Color.FromArgb(232, 252, 232), false));
                    j--;
                }
            }

            diffRows.Reverse();

            SendMessage(txtSourceDdl.Handle, WM_SETREDRAW, 0, 0);
            SendMessage(txtTargetDdl.Handle, WM_SETREDRAW, 0, 0);

            foreach (var row in diffRows)
            {
                AppendDiffLine(txtSourceDdl, txtSourceLineNumbers, row.s, row.sCol, ref sLineNum);
                AppendDiffLine(txtTargetDdl, txtTargetLineNumbers, row.t, row.tCol, ref tLineNum);
            }

            HighlightSqlKeywords(txtSourceDdl);
            HighlightSqlKeywords(txtTargetDdl);

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

            SendMessage(txtSourceDdl.Handle, WM_SETREDRAW, 1, 0);
            SendMessage(txtTargetDdl.Handle, WM_SETREDRAW, 1, 0);

            txtSourceDdl.Invalidate();
            txtTargetDdl.Invalidate();
        }

        private void AppendDiffLine(RichTextBox rtb, RichTextBox gutter, string? text, Color backColor, ref int lineNum)
        {
            int start = rtb.TextLength;
            string displayText = (text ?? "");
            rtb.AppendText(displayText + "\n");

            if (text != null)
                gutter.AppendText($"{lineNum++}\n");
            else
                gutter.AppendText("~\n");

            int end = rtb.TextLength;
            rtb.Select(start, end - start);
            rtb.SelectionBackColor = backColor;

            if (backColor != Color.White)
            {
                rtb.SelectionColor = Color.FromArgb(40, 40, 60);
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            }
            rtb.DeselectAll();
        }

        private bool IsLikelyChange(string? s, string? t)
        {
            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(t)) return false;
            string s1 = s.Trim().TrimEnd(',', ';');
            string t1 = t.Trim().TrimEnd(',', ';');
            if (s1 == t1) return true;

            var sWords = s1.Split(new[] { ' ', '(', '"', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var tWords = t1.Split(new[] { ' ', '(', '"', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (sWords.Length == 0 || tWords.Length == 0) return false;

            if (sWords[0] != tWords[0]) return false;

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CREATE", "ALTER", "DROP", "GRANT", "REVOKE" };
            if (keywords.Contains(sWords[0]))
            {
                if (sWords.Length > 1 && tWords.Length > 1)
                {
                    if (sWords[1] != tWords[1]) return false;
                }
                else if (sWords.Length != tWords.Length)
                {
                    return false;
                }
            }

            return true;
        }

        private void HighlightInLineDiff(RichTextBox rtb, int lineStart, string s1, string s2)
        {
            int prefixLen = 0;
            while (prefixLen < s1.Length && prefixLen < s2.Length && s1[prefixLen] == s2[prefixLen])
                prefixLen++;

            int suffixLen = 0;
            while (suffixLen < s1.Length - prefixLen && suffixLen < s2.Length - prefixLen && 
                   s1[s1.Length - 1 - suffixLen] == s2[s2.Length - 1 - suffixLen])
                suffixLen++;

            int diffStart = lineStart + prefixLen;
            int diffLen = s1.Length - prefixLen - suffixLen;

            if (diffLen > 0)
            {
                rtb.Select(diffStart, diffLen);
                rtb.SelectionColor = Color.DarkRed;
                rtb.SelectionBackColor = Color.FromArgb(255, 255, 180);
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            }
        }

        private void HighlightSqlKeywords(RichTextBox rtb)
        {
            var keywords = new[] {
                "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "DROP", "SCHEMA", "TABLE", "VIEW",
                "PROCEDURE", "FUNCTION", "ROUTINE", "IF", "EXISTS", "CASCADE", "RESTRICT", "JOIN", "ON",
                "IN", "NOT", "NULL", "AND", "OR", "LIKE", "LIMIT", "GROUP", "BY", "ORDER", "CREATE", "ALTER",
                "ADD", "COLUMN", "CONSTRAINT", "PRIMARY", "KEY", "UNIQUE", "INDEX", "FOREIGN", "REFERENCES"
            };

            string text = rtb.Text;
            foreach (var word in keywords)
            {
                HighlightWord(rtb, word, Color.Blue, true);
            }
        }

        private void HighlightWord(RichTextBox rtb, string word, Color color, bool bold)
        {
            int index = 0;
            while (true)
            {
                index = rtb.Text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
                if (index == -1) break;

                bool startBoundary = (index == 0 || !char.IsLetterOrDigit(rtb.Text[index - 1]) && rtb.Text[index - 1] != '_');
                bool endBoundary = (index + word.Length == rtb.Text.Length || !char.IsLetterOrDigit(rtb.Text[index + word.Length]) && rtb.Text[index + word.Length] != '_');

                if (startBoundary && endBoundary)
                {
                    rtb.Select(index, word.Length);
                    rtb.SelectionColor = color;
                    if (bold) rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                }
                index += word.Length;
            }
        }
    }
}
