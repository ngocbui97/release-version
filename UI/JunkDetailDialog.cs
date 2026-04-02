using System;
using System.Drawing;
using System.Windows.Forms;
using ReleasePrepTool.Models;
using ReleasePrepTool.Services;

namespace ReleasePrepTool.UI
{
    public partial class JunkDetailDialog : Form
    {
        private readonly JunkItem _item;
        private readonly PostgresService? _pgService;

        public JunkDetailDialog(JunkItem item, PostgresService? pgService = null)
        {
            _item = item;
            _pgService = pgService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = $"Junk Detail: {_item.ObjectName}";
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;

            var pnlMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            
            var lblInfo = new Label { 
                Text = $"Type: {_item.Type}\nLocation: {_item.SchemaName}.{_item.ObjectName}\nReason: {_item.DetectedContent}",
                Dock = DockStyle.Top,
                Height = 80,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var lblDataHeader = new Label { 
                Text = "Raw Content (Junk highlighted):", 
                Dock = DockStyle.Top, 
                Height = 25, 
                Margin = new Padding(0, 10, 0, 0) 
            };

            var rtbContent = new RichTextBox { 
                Dock = DockStyle.Fill, 
                ReadOnly = true, 
                BackColor = Color.White,
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnClose = new Button { 
                Text = "Close", 
                Dock = DockStyle.Bottom, 
                Height = 35, 
                DialogResult = DialogResult.OK 
            };

            pnlMain.Controls.Add(rtbContent);
            pnlMain.Controls.Add(lblDataHeader);
            pnlMain.Controls.Add(lblInfo);
            pnlMain.Controls.Add(btnClose);

            this.Controls.Add(pnlMain);

            this.Load += async (s, e) => {
                if (_item.Type == JunkType.DataRecord && _pgService != null)
                {
                    rtbContent.Text = "Fetching full record details...";
                    var fullRow = await _pgService.GetFullRowDataAsync(_item.DatabaseName ?? "", _item.SchemaName ?? "public", _item.ObjectName ?? "", _item.PrimaryKeyColumn ?? "", _item.PrimaryKeyValue ?? "");
                    
                    if (fullRow.Count > 0)
                    {
                        rtbContent.Clear();
                        foreach (var kv in fullRow)
                        {
                            int start = rtbContent.TextLength;
                            bool isJunkCol = kv.Key.Equals(_item.ColumnName, StringComparison.OrdinalIgnoreCase);
                            
                            rtbContent.AppendText($"{kv.Key}: ");
                            rtbContent.Select(start, kv.Key.Length);
                            rtbContent.SelectionFont = new Font(rtbContent.Font, FontStyle.Bold);
                            if (isJunkCol) rtbContent.SelectionColor = Color.DarkRed;
                            
                            rtbContent.AppendText($"{kv.Value}\n");
                            if (isJunkCol) {
                                // Highlight keywords in the junk value
                                HighlightKeywordsInCurrentRange(rtbContent, start + kv.Key.Length + 2, kv.Value.Length, _item.MatchedKeywords);
                            }
                        }
                    }
                    else
                    {
                        HighlightKeywords(rtbContent, _item.RawData ?? "", _item.MatchedKeywords);
                    }
                }
                else
                {
                    HighlightKeywords(rtbContent, _item.RawData ?? "", _item.MatchedKeywords);
                }
            };
        }

        private void HighlightKeywordsInCurrentRange(RichTextBox rtb, int offset, int length, System.Collections.Generic.List<string>? keywords)
        {
            if (keywords == null) return;
            string valueText = rtb.Text.Substring(offset, length);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                int idx = 0;
                while ((idx = valueText.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    rtb.Select(offset + idx, kw.Length);
                    rtb.SelectionBackColor = Color.Yellow;
                    rtb.SelectionColor = Color.Red;
                    idx += kw.Length;
                }
            }
            rtb.DeselectAll();
        }

        private void PopulateData()
        {
            // Initial label text already set in InitializeComponent
        }

        private void HighlightKeywords(RichTextBox rtb, string text, List<string> keywords)
        {
            rtb.Text = text;
            if (keywords == null || !keywords.Any()) return;

            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                int index = 0;
                while ((index = rtb.Text.IndexOf(kw, index, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    rtb.Select(index, kw.Length);
                    rtb.SelectionBackColor = Color.Yellow;
                    rtb.SelectionColor = Color.Red;
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                    index += kw.Length;
                }
            }
            rtb.DeselectAll();
        }
    }
}
