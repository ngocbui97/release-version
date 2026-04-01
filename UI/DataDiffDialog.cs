using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ReleasePrepTool.Services;

namespace ReleasePrepTool.UI
{
    public class DataDiffDialog : Form
    {
        private string _tableName = default!;
        private List<DataRowDiff> _diffs = default!;
        private Label lblSummary = default!;
        private TabControl tabControl = default!;
        private Panel pnlEmpty = default!;

        public DataDiffDialog(string tableName, List<DataRowDiff> diffs)
        {
            _tableName = tableName;
            _diffs = diffs ?? new List<DataRowDiff>();
            InitializeComponent();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = $"Data Differences — {_tableName}";
            // Fit within current screen working area
            var screen = Screen.FromControl(this).WorkingArea;
            this.Size = new Size(Math.Min(1300, screen.Width - 60), Math.Min(750, screen.Height - 60));
            this.MinimumSize = new Size(900, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimizeBox = true;
            this.MaximizeBox = true;

            // Header with summary + a Close button
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(14, 10, 14, 10), BackColor = Color.FromArgb(245, 247, 252) };
            lblSummary = new Label {
                Text = $"Table: {_tableName}  |  Loading…",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 80),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var btnClose = new Button {
                Text = "✕ Close",
                Dock = DockStyle.Right,
                Width = 90,
                Height = 30,
                Margin = new Padding(0, 10, 0, 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnClose.Click += (s, e) => this.Close();
            pnlHeader.Controls.Add(lblSummary);
            pnlHeader.Controls.Add(btnClose);

            // Separator
            var sep = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Color.FromArgb(210, 215, 230) };

            // TabControl
            tabControl = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5), Font = new Font("Segoe UI", 9.5f) };

            // Empty state panel
            pnlEmpty = new Panel { Dock = DockStyle.Fill, Visible = false };
            var lblEmpty = new Label {
                Text = "✅  No record differences found for this table.\nAll rows are identical between Source and Target.",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12f, FontStyle.Regular),
                ForeColor = Color.FromArgb(60, 140, 80),
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlEmpty.Controls.Add(lblEmpty);

            this.Controls.Add(tabControl);
            this.Controls.Add(pnlEmpty);
            this.Controls.Add(sep);
            this.Controls.Add(pnlHeader);
        }

        private void LoadData()
        {
            if (!_diffs.Any())
            {
                lblSummary.Text = $"Table: {_tableName}  |  ✅ No differences found";
                pnlEmpty.Visible = true;
                tabControl.Visible = false;
                return;
            }

            var added   = _diffs.Count(d => d.DiffType == "Added");
            var removed = _diffs.Count(d => d.DiffType == "Removed");
            var changed = _diffs.Count(d => d.DiffType == "Changed");
            var same    = _diffs.Count(d => d.DiffType == "Same");

            // Changed tab: special side-by-side view
            if (changed > 0) AddChangedTab(changed);
            if (added > 0)   AddSimpleTab($"➕ Added ({added})",    "Added",   Color.FromArgb(235, 255, 235));
            if (removed > 0) AddSimpleTab($"➖ Removed ({removed})", "Removed", Color.FromArgb(255, 235, 235));
            if (same > 0)    AddSimpleTab($"✅ Identical ({same})",  "Same",    Color.FromArgb(248, 250, 248));

            lblSummary.Text = $"Table: {_tableName}  |  🔄 {changed} Changed   ➕ {added} Added   ➖ {removed} Removed   ✅ {same} Identical";
            tabControl.Visible = true;
            pnlEmpty.Visible = false;
        }

        /// <summary>
        /// Special tab for Changed records: each changed record shows two rows — Source (New) vs Target (Old).
        /// Changed cells are highlighted in contrasting colors.
        /// </summary>
        private void AddChangedTab(int count)
        {
            var filteredDiffs = _diffs.Where(d => d.DiffType == "Changed").Take(1000).ToList();

            // Collect all columns
            var allCols = new HashSet<string>();
            foreach (var d in filteredDiffs)
            {
                foreach (var k in d.SourceData.Keys) allCols.Add(k);
                foreach (var k in d.TargetData.Keys) allCols.Add(k);
            }
            var sortedCols = allCols.OrderBy(c => c).ToList();

            var tabPage = new TabPage($"🔄 Changed ({count})") { Padding = new Padding(0) };

            var grid = BuildGrid();
            // First column: row type label
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = "_RowType", HeaderText = "Type", Width = 80,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) }
            });
            // Changed columns summary
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = "_ChangedCols", HeaderText = "Changed Columns", Width = 180,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });
            // Data columns
            foreach (var col in sortedCols)
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = col, HeaderText = col });

            var colorNewRow = Color.FromArgb(220, 245, 255); // light blue = new/source
            var colorOldRow = Color.FromArgb(255, 240, 220); // light amber = old/target
            var cellChanged = Color.FromArgb(255, 60, 60);

            foreach (var d in filteredDiffs)
            {
                var changedColsLabel = string.Join(", ", d.ChangedColumns);

                // --- SOURCE (New) row ---
                var srcIdx = grid.Rows.Add();
                var srcRow = grid.Rows[srcIdx];
                srcRow.DefaultCellStyle.BackColor = colorNewRow;
                srcRow.Cells["_RowType"].Value = "🔵 New";
                srcRow.Cells["_ChangedCols"].Value = changedColsLabel;
                foreach (var col in sortedCols)
                {
                    if (!grid.Columns.Contains(col)) continue;
                    var val = d.SourceData.ContainsKey(col) ? d.SourceData[col] : null;
                    srcRow.Cells[col].Value = val == null || val is DBNull ? "NULL" : val.ToString();
                    if (d.ChangedColumns.Contains(col))
                    {
                        srcRow.Cells[col].Style.BackColor = Color.FromArgb(180, 225, 255);
                        srcRow.Cells[col].Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                        srcRow.Cells[col].Style.ForeColor = Color.FromArgb(0, 50, 130);
                    }
                }

                // --- TARGET (Old) row ---
                var tgtIdx = grid.Rows.Add();
                var tgtRow = grid.Rows[tgtIdx];
                tgtRow.DefaultCellStyle.BackColor = colorOldRow;
                tgtRow.Cells["_RowType"].Value = "🟡 Old";
                tgtRow.Cells["_ChangedCols"].Value = ""; // blank for old row
                foreach (var col in sortedCols)
                {
                    if (!grid.Columns.Contains(col)) continue;
                    var val = d.TargetData.ContainsKey(col) ? d.TargetData[col] : null;
                    tgtRow.Cells[col].Value = val == null || val is DBNull ? "NULL" : val.ToString();
                    if (d.ChangedColumns.Contains(col))
                    {
                        tgtRow.Cells[col].Style.BackColor = Color.FromArgb(255, 215, 160);
                        tgtRow.Cells[col].Style.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                        tgtRow.Cells[col].Style.ForeColor = Color.FromArgb(130, 50, 0);
                    }
                }

                // Add a thin separator row between records
                var sepIdx = grid.Rows.Add();
                grid.Rows[sepIdx].DefaultCellStyle.BackColor = Color.FromArgb(210, 215, 225);
                grid.Rows[sepIdx].Height = 4;
                foreach (DataGridViewCell cell in grid.Rows[sepIdx].Cells) cell.ReadOnly = true;
            }

            AddLimitNotice(tabPage, filteredDiffs.Count, 1000);
            tabPage.Controls.Add(grid);
            tabControl.TabPages.Add(tabPage);
        }

        private void AddSimpleTab(string title, string diffType, Color rowBgColor)
        {
            var filteredDiffs = _diffs.Where(d => d.DiffType == diffType).Take(2000).ToList();
            if (!filteredDiffs.Any()) return;

            var allCols = new HashSet<string>();
            foreach (var d in filteredDiffs)
            {
                foreach (var k in d.SourceData.Keys) allCols.Add(k);
                foreach (var k in d.TargetData.Keys) allCols.Add(k);
            }
            var sortedCols = allCols.OrderBy(c => c).ToList();

            var tabPage = new TabPage(title) { Padding = new Padding(0) };
            var grid = BuildGrid();

            foreach (var col in sortedCols)
                grid.Columns.Add(col, col);

            foreach (var d in filteredDiffs)
            {
                var data = (diffType == "Added") ? d.TargetData : d.SourceData;
                var rowIdx = grid.Rows.Add();
                var row = grid.Rows[rowIdx];
                row.DefaultCellStyle.BackColor = rowBgColor;
                foreach (var col in sortedCols)
                {
                    if (!grid.Columns.Contains(col)) continue;
                    var val = data.ContainsKey(col) ? data[col] : null;
                    row.Cells[col].Value = val == null || val is DBNull ? "NULL" : (val.ToString() ?? "");
                }
            }

            AddLimitNotice(tabPage, filteredDiffs.Count, 2000);
            tabPage.Controls.Add(grid);
            tabControl.TabPages.Add(tabPage);
        }

        private DataGridView BuildGrid()
        {
            return new DataGridView {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = Color.White,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                    BackColor = Color.FromArgb(230, 235, 245),
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(50, 50, 80),
                    Padding = new Padding(4)
                },
                EnableHeadersVisualStyles = false,
                RowTemplate = { Height = 24 }
            };
        }

        private void AddLimitNotice(TabPage page, int count, int limit)
        {
            if (count < limit) return;
            var noticePanel = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(255, 250, 220) };
            noticePanel.Controls.Add(new Label {
                Text = $"⚠️  Showing first {limit:N0} records. Export the sync script to see all differences.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.5f), ForeColor = Color.DarkOrange
            });
            page.Controls.Add(noticePanel);
        }
    }
}
