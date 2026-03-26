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
        private DataGridView dgvDiff;
        private string _tableName;
        private List<DataRowDiff> _diffs;
        private Label lblSummary;

        public DataDiffDialog(string tableName, List<DataRowDiff> diffs)
        {
            _tableName = tableName;
            _diffs = diffs;
            InitializeComponent();
            LoadData();
        }

        private void InitializeComponent()
        {
            this.Text = $"Data Differences: {_tableName}";
            this.Size = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterParent;

            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(10), BackColor = Color.GhostWhite };
            lblSummary = new Label { 
                Text = $"Table: {_tableName} | Calculating summary...", 
                Dock = DockStyle.Fill, 
                Font = new Font("Segoe UI", 10f, FontStyle.Regular)
            };
            pnlHeader.Controls.Add(lblSummary);

            dgvDiff = new DataGridView { 
                Dock = DockStyle.Fill, 
                ReadOnly = true, 
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = Color.White,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };

            this.Controls.Add(dgvDiff);
            this.Controls.Add(pnlHeader);
        }

        private void LoadData()
        {
            if (!_diffs.Any()) return;

            // Determine all columns across source and target
            var allCols = new HashSet<string>();
            foreach (var d in _diffs)
            {
                foreach (var k in d.SourceData.Keys) allCols.Add(k);
                foreach (var k in d.TargetData.Keys) allCols.Add(k);
            }

            dgvDiff.Columns.Add("DiffType", "Status");
            foreach (var col in allCols.OrderBy(c => c))
            {
                dgvDiff.Columns.Add(col, col);
            }

            foreach (var d in _diffs)
            {
                var rowIdx = dgvDiff.Rows.Add();
                var row = dgvDiff.Rows[rowIdx];
                row.Cells["DiffType"].Value = d.DiffType;

                Color rowColor = Color.White;
                switch (d.DiffType)
                {
                    case "Added": rowColor = Color.FromArgb(230, 255, 230); break; // Light Green
                    case "Removed": rowColor = Color.FromArgb(255, 230, 230); break; // Light Pink
                    case "Changed": rowColor = Color.FromArgb(230, 240, 255); break; // Light Blue
                }
                row.DefaultCellStyle.BackColor = rowColor;

                var data = d.DiffType == "Removed" ? d.SourceData : d.TargetData;
                foreach (var col in allCols)
                {
                    if (data.ContainsKey(col))
                    {
                        var val = data[col];
                        row.Cells[col].Value = val == null || val is DBNull ? "NULL" : val.ToString();
                        
                        // Highlight specific changed cells
                        if (d.DiffType == "Changed" && d.ChangedColumns.Contains(col))
                        {
                            row.Cells[col].Style.ForeColor = Color.Red;
                            row.Cells[col].Style.Font = new Font(dgvDiff.Font, FontStyle.Bold);
                        }
                    }
                }
            }

            var added = _diffs.Count(d => d.DiffType == "Added");
            var removed = _diffs.Count(d => d.DiffType == "Removed");
            var changed = _diffs.Count(d => d.DiffType == "Changed");
            var same = _diffs.Count(d => d.DiffType == "Same");

            lblSummary.Text = $"Table: {_tableName}\n" +
                              $"📊 Summary: {added} Added, {removed} Removed, {changed} Changed, {same} Identical";
        }
    }
}
