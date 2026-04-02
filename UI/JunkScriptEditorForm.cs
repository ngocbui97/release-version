using System;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace ReleasePrepTool.UI
{
    public partial class JunkScriptEditorForm : Form
    {
        private RichTextBox _rtbSql;
        public string EditedScript { get; private set; } = "";

        public JunkScriptEditorForm(string initialScript)
        {
            InitializeComponent();
            _rtbSql.Text = initialScript;
            ApplySyntaxHighlighting();
        }

        private void InitializeComponent()
        {
            this.Text = "Review & Edit Junk Cleanup Script (Postgres)";
            this.Size = new Size(1000, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(240, 240, 240) };
            var lblHint = new Label { 
                Text = "💡 Review the script before proceeding. You can edit the SQL manually if needed.", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleLeft, 
                Padding = new Padding(10, 0, 0, 0),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Italic)
            };
            pnlTop.Controls.Add(lblHint);

            var pnlBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 10, 10, 10) };
            
            var btnCancel = new Button { Text = "Cancel", Width = 120, Height = 35 };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            var btnApply = new Button { 
                Text = "Apply & Execute", 
                Width = 180, 
                Height = 40, 
                BackColor = Color.FromArgb(0, 122, 204), 
                ForeColor = Color.White, 
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnApply.Click += (s, e) => {
                this.EditedScript = _rtbSql.Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            pnlBottom.Controls.Add(btnCancel);
            pnlBottom.Controls.Add(btnApply);

            _rtbSql = new RichTextBox {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 11f),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                AcceptsTab = true
            };

            // Custom event for real-time highlighting (throttled)
            _rtbSql.TextChanged += (s, e) => {
                // Throttle this or use a timer to avoid lag on huge scripts
            };

            this.Controls.Add(_rtbSql);
            this.Controls.Add(pnlTop);
            this.Controls.Add(pnlBottom);
        }

        private void ApplySyntaxHighlighting()
        {
            // Simple regex-based syntax highlighter
            string text = _rtbSql.Text;
            int selStart = _rtbSql.SelectionStart;
            int selLen = _rtbSql.SelectionLength;

            _rtbSql.SelectAll();
            _rtbSql.SelectionColor = Color.Gainsboro; // Default text color

            // Keywords
            string keywords = @"\b(SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|DROP|SCHEMA|TABLE|VIEW|PROCEDURE|FUNCTION|ROUTINE|IF|EXISTS|CASCADE|RESTRICT|JOIN|ON|IN|NOT|NULL|AND|OR|ILIKE|LIKE|LIMIT|GROUP|BY|ORDER|BY|CREATE|DATABASE|SET|USE|ALTER)\b";
            MatchSelection(keywords, Color.FromArgb(86, 156, 214)); // Blue

            // Strings
            string strings = @"'[^']*'";
            MatchSelection(strings, Color.FromArgb(214, 157, 133)); // Salmon

            // Comments
            string comments = @"--.*$";
            MatchSelection(comments, Color.FromArgb(106, 153, 85), true); // Green

            _rtbSql.Select(selStart, selLen);
            _rtbSql.SelectionColor = Color.Gainsboro;
        }

        private void MatchSelection(string pattern, Color color, bool isMultiline = false)
        {
            var options = RegexOptions.IgnoreCase;
            if (isMultiline) options |= RegexOptions.Multiline;

            foreach (Match match in Regex.Matches(_rtbSql.Text, pattern, options))
            {
                _rtbSql.Select(match.Index, match.Length);
                _rtbSql.SelectionColor = color;
                if (color == Color.FromArgb(86, 156, 214)) // Bold keywords
                    _rtbSql.SelectionFont = new Font(_rtbSql.Font, FontStyle.Bold);
            }
        }
    }
}
