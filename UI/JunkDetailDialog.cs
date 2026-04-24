using System;
using System.Drawing;
using System.Linq;
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
            this.Text = $"Junk Detail — {_item.SchemaName}.{_item.ObjectName}";
            this.Size = new Size(1000, 700);
            this.MinimumSize = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.White;

            // ── Header strip ──────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54, // Ultra compact header
                BackColor = Color.FromArgb(30, 30, 46),
                Padding = new Padding(18, 12, 18, 12)
            };

            string typeIcon = _item.Type switch
            {
                JunkType.DataRecord => "🗃",
                JunkType.Table      => "📋",
                JunkType.View       => "👁",
                JunkType.Column     => "🔷",
                JunkType.Routine    => "⚙",
                _                   => "📦"
            };

            var pnlHeaderLeft = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            var lblTitle = new Label
            {
                Text = $"{typeIcon}  {_item.SchemaName}.{_item.ObjectName}",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold), // slightly smaller to fit in line
                AutoSize = true,
                Margin = new Padding(0, 0, 15, 0)
            };
            
            string subtitleText = _item.Type == JunkType.DataRecord
                ? $"PK: {_item.PrimaryKeyColumn} = {_item.PrimaryKeyValue}"
                : $"Type: {_item.Type}   |   OID: {_item.Oid}";
                
            var lblSubtitle = new Label
            {
                Text = subtitleText,
                ForeColor = Color.FromArgb(180, 180, 200),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0) // Align baseline with title
            };
            
            pnlHeaderLeft.Controls.Add(lblTitle);
            pnlHeaderLeft.Controls.Add(lblSubtitle);

            var lblReason = new Label
            {
                Text = $"  ⚠  {_item.DetectedContent}",
                ForeColor = Color.FromArgb(255, 230, 120),
                BackColor = Color.FromArgb(80, 50, 10),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 400,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0)
            };

            pnlHeader.Controls.Add(pnlHeaderLeft);
            pnlHeader.Controls.Add(lblReason);

            // ── Tab control ───────────────────────────────────────────────
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f)
            };

            var tabRecord   = new TabPage("📋  Record Data") { BackColor = Color.White };
            var tabCascade  = new TabPage("⚠  Cascade Impact") { BackColor = Color.White };
            var tabScript   = new TabPage("🗑  Delete Script") { BackColor = Color.White };

            // ── Tab 1: Record Data ─────────────────────────────────────────
            var rtbRecord = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(250, 250, 252),
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            tabRecord.Controls.Add(rtbRecord);

            // ── Tab 2: Cascade Impact ──────────────────────────────────────
            var splitCascade = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 180, // Top panel gets 180px height
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(220, 220, 225) // subtle border color
            };
            splitCascade.Panel1.BackColor = Color.White;
            splitCascade.Panel2.BackColor = Color.White;
            splitCascade.Panel1.Padding = new Padding(0, 5, 0, 0);
            
            var tvCascade = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None,
                ShowLines = true,
                ShowPlusMinus = true,
                FullRowSelect = true,
                ShowNodeToolTips = true
            };
            
            var pnlRight = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0, 0, 0, 12) };
            var lblRightTitle = new Label 
            { 
                Text = "Detail View", 
                Dock = DockStyle.Top, 
                Height = 32, 
                TextAlign = ContentAlignment.MiddleLeft, 
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 80),
                BackColor = Color.FromArgb(245, 245, 250),
                Padding = new Padding(10, 0, 0, 0),
                Visible = false
            };

            var dgvCascadeDetail = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(230, 230, 230),
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                Visible = false
            };
            dgvCascadeDetail.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            
            var lblCascadeHint = new Label 
            { 
                Text = "Select a record or table on the left to view data.", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.MiddleCenter, 
                ForeColor = Color.Gray 
            };
            
            pnlRight.Controls.Add(dgvCascadeDetail);
            pnlRight.Controls.Add(lblRightTitle);
            pnlRight.Controls.Add(lblCascadeHint);
            
            splitCascade.Panel1.Controls.Add(tvCascade);
            splitCascade.Panel2.Controls.Add(pnlRight);

            var lblNoCascade = new Label
            {
                Text = "✅  No cascade dependencies found.\n\nThis record is not referenced by any foreign key in other tables.\nIt is safe to delete without affecting other records.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(80, 150, 80),
                Visible = false
            };
            tabCascade.Controls.Add(splitCascade);
            tabCascade.Controls.Add(lblNoCascade);

            // ── Tab 3: Delete Script ───────────────────────────────────────
            var rtbScript = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 15, 25),
                ForeColor = Color.FromArgb(200, 230, 200),
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            var pnlScriptToolbar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(30, 30, 46) };
            var btnCopyScript = new Button
            {
                Text = "📋  Copy Script",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 120, 200),
                Font = new Font("Segoe UI", 8.5f),
                Height = 26,
                Width = 120,
                Location = new Point(8, 4)
            };
            btnCopyScript.FlatAppearance.BorderSize = 0;
            btnCopyScript.Click += (s, e) => {
                if (!string.IsNullOrEmpty(rtbScript.Text)) Clipboard.SetText(rtbScript.Text);
            };
            pnlScriptToolbar.Controls.Add(btnCopyScript);
            tabScript.Controls.Add(rtbScript);
            tabScript.Controls.Add(pnlScriptToolbar);

            tabs.TabPages.AddRange(new[] { tabRecord, tabCascade, tabScript });

            this.Controls.Add(tabs);
            this.Controls.Add(pnlHeader);

            // Allow closing via Escape key since we removed the footer close button to save space
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };

            // ── Load data async ────────────────────────────────────────────
            this.Load += async (sender, args) =>
            {
                // Tab 1: Record Data
                if (_pgService != null && _item.Type == JunkType.DataRecord
                    && !string.IsNullOrEmpty(_item.PrimaryKeyColumn)
                    && !string.IsNullOrEmpty(_item.PrimaryKeyValue))
                {
                    rtbRecord.Text = "⏳ Loading record data...";
                    try
                    {
                        var fullRow = await _pgService.GetFullRowDataAsync(
                            _item.DatabaseName ?? "",
                            _item.SchemaName ?? "public",
                            _item.ObjectName ?? "",
                            _item.PrimaryKeyColumn,
                            _item.PrimaryKeyValue);

                        if (fullRow.Count > 0)
                        {
                            // Create DataGridView dynamically if doesn't exist to show tabular data
                            DataGridView dgvRecord = rtbRecord.Parent!.Controls.OfType<DataGridView>().FirstOrDefault()!;
                            if (dgvRecord == null)
                            {
                                dgvRecord = new DataGridView
                                {
                                    Dock = DockStyle.Fill,
                                    AllowUserToAddRows = false,
                                    AllowUserToDeleteRows = false,
                                    ReadOnly = false, // Allow edit mode to select text
                                    RowHeadersVisible = false,
                                    BackgroundColor = Color.White,
                                    BorderStyle = BorderStyle.None,
                                    CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                                    GridColor = Color.FromArgb(230, 230, 230),
                                    SelectionMode = DataGridViewSelectionMode.CellSelect, // Allow single cell click
                                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                                };
                                dgvRecord.Columns.Add("ColName", "Column");
                                dgvRecord.Columns.Add("ColValue", "Value");
                                dgvRecord.Columns[0].Width = 150;
                                dgvRecord.Columns[0].ReadOnly = true; // Name is read-only
                                dgvRecord.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                                dgvRecord.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                                dgvRecord.Columns[0].DefaultCellStyle.ForeColor = Color.FromArgb(60, 100, 160);
                                dgvRecord.Columns[1].ReadOnly = false; // Value allows edit mode for copying
                                dgvRecord.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                                dgvRecord.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                                
                                // Custom painting for keyword highlight
                                dgvRecord.CellPainting += (s, e) => {
                                    if (e.RowIndex >= 0 && e.ColumnIndex == 1 && e.Value != null && _item.MatchedKeywords != null)
                                    {
                                        string val = e.Value.ToString() ?? "";
                                        bool isJunkCol = dgvRecord.Rows[e.RowIndex].Cells[0].Value?.ToString()?.Equals(_item.ColumnName, StringComparison.OrdinalIgnoreCase) == true;
                                        if (isJunkCol)
                                        {
                                            e.PaintBackground(e.CellBounds, true);
                                            e.Graphics.DrawString(val, e.CellStyle.Font, Brushes.Black, e.CellBounds.X + 2, e.CellBounds.Y + 4);
                                            
                                            // Simple highlight logic
                                            foreach (var kw in _item.MatchedKeywords)
                                            {
                                                if (string.IsNullOrEmpty(kw)) continue;
                                                int idx = val.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                                                if (idx >= 0)
                                                {
                                                    // rough highlight position
                                                    var strBefore = val.Substring(0, idx);
                                                    var sizeBefore = e.Graphics.MeasureString(strBefore, e.CellStyle.Font);
                                                    var sizeMatch = e.Graphics.MeasureString(val.Substring(idx, kw.Length), e.CellStyle.Font);
                                                    e.Graphics.FillRectangle(Brushes.Yellow, e.CellBounds.X + 2 + sizeBefore.Width - 4, e.CellBounds.Y + 4, sizeMatch.Width, sizeMatch.Height);
                                                    e.Graphics.DrawString(val.Substring(idx, kw.Length), new Font(e.CellStyle.Font, FontStyle.Bold), Brushes.DarkRed, e.CellBounds.X + 2 + sizeBefore.Width - 4, e.CellBounds.Y + 4);
                                                }
                                            }
                                            e.Handled = true;
                                        }
                                    }
                                };
                                rtbRecord.Parent.Controls.Add(dgvRecord);
                            }
                            
                            rtbRecord.Visible = false;
                            dgvRecord.Visible = true;
                            dgvRecord.Rows.Clear();
                            
                            foreach (var kv in fullRow)
                            {
                                int rIdx = dgvRecord.Rows.Add(kv.Key, kv.Value);
                                bool isJunkCol = kv.Key.Equals(_item.ColumnName, StringComparison.OrdinalIgnoreCase);
                                if (isJunkCol)
                                {
                                    dgvRecord.Rows[rIdx].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 240);
                                    dgvRecord.Rows[rIdx].Cells[0].Style.ForeColor = Color.DarkRed;
                                }
                            }
                            dgvRecord.ClearSelection();
                        }
                        else
                        {
                            rtbRecord.Visible = true;
                            rtbRecord.Text = "(No data found — record may have been deleted)";
                            var d = rtbRecord.Parent!.Controls.OfType<DataGridView>().FirstOrDefault();
                            if (d != null) d.Visible = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        rtbRecord.Visible = true;
                        rtbRecord.Text = $"❌ Error loading record: {ex.Message}";
                        var d = rtbRecord.Parent!.Controls.OfType<DataGridView>().FirstOrDefault();
                        if (d != null) d.Visible = false;
                    }
                }
                else if (_pgService != null && _item.Type != JunkType.DataRecord)
                {
                    rtbRecord.Text = "⏳ Loading object definition...";
                    try
                    {
                        var def = await _pgService.GetObjectDefinitionAsync(
                            _item.DatabaseName ?? "",
                            _item.SchemaName ?? "public",
                            _item.ObjectName ?? "",
                            _item.Type, _item.Oid);
                        _item.RawData = def;
                        HighlightKeywords(rtbRecord, def, _item.MatchedKeywords);
                    }
                    catch (Exception ex)
                    {
                        rtbRecord.Text = $"❌ Error: {ex.Message}";
                    }
                }
                else
                {
                    HighlightKeywords(rtbRecord, _item.RawData ?? "(No data)", _item.MatchedKeywords);
                }

                // Tab 2: Cascade Impact
                if (_item.DependentObjects.Any())
                {
                    lblNoCascade.Visible = false;
                    splitCascade.Visible = true;
                    BuildCascadeTree(tvCascade, _item);
                    tvCascade.ExpandAll();
                    if (tvCascade.Nodes.Count > 0 && tvCascade.Nodes[0].Nodes.Count > 0)
                    {
                        tvCascade.SelectedNode = tvCascade.Nodes[0].Nodes[0];
                    }
                }
                else
                {
                    splitCascade.Visible = false;
                    lblNoCascade.Visible = true;
                }

                // Tab 3: Delete Script
                rtbScript.Text = GenerateDeleteScript(_item);
                
                // Double click handler for Cascade tree to view row details
                tvCascade.NodeMouseDoubleClick += (s, e) => {
                    if (e.Node?.Tag is JunkItem clickedItem && !string.IsNullOrEmpty(clickedItem.PrimaryKeyValue))
                    {
                        var detailDlg = new JunkDetailDialog(clickedItem, _pgService);
                        detailDlg.Show(this);
                    }
                };

                // Single click (Select) handler to show data in right pane
                tvCascade.AfterSelect += async (s, e) => {
                    if (_pgService == null) return;
                    if (e.Node?.Tag is JunkItem clickedItem)
                    {
                        dgvCascadeDetail.Visible = false;
                        lblCascadeHint.Visible = true;
                        lblCascadeHint.Text = "⏳ Loading data...";
                        
                        try 
                        {
                            if (!string.IsNullOrEmpty(clickedItem.FkColumn) && !string.IsNullOrEmpty(clickedItem.FkValue))
                            {
                                lblRightTitle.Text = $"Affected Records: {clickedItem.SchemaName}.{clickedItem.ObjectName}";
                                lblRightTitle.Visible = true;
                                // Table node: Load all affected rows
                                var rows = await _pgService.GetRowsByFkAsync(clickedItem.DatabaseName ?? "", clickedItem.SchemaName ?? "public", clickedItem.ObjectName ?? "", clickedItem.FkColumn, clickedItem.FkValue);
                                dgvCascadeDetail.Columns.Clear();
                                dgvCascadeDetail.Rows.Clear();
                                dgvCascadeDetail.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
                                if (rows.Count > 0)
                                {
                                    foreach (var key in rows[0].Keys) dgvCascadeDetail.Columns.Add(key, key);
                                    foreach (var row in rows)
                                    {
                                        var values = new object[row.Count];
                                        int i = 0;
                                        foreach (var val in row.Values) values[i++] = val;
                                        dgvCascadeDetail.Rows.Add(values);
                                    }
                                    dgvCascadeDetail.Visible = true;
                                    lblCascadeHint.Visible = false;
                                }
                                else lblCascadeHint.Text = "(No records found)";
                            }
                            else if (!string.IsNullOrEmpty(clickedItem.PrimaryKeyValue))
                            {
                                lblRightTitle.Text = $"Record Detail: id = {clickedItem.PrimaryKeyValue}";
                                lblRightTitle.Visible = true;
                                // Row node: Load single row details vertically
                                var row = await _pgService.GetFullRowDataAsync(clickedItem.DatabaseName ?? "", clickedItem.SchemaName ?? "public", clickedItem.ObjectName ?? "", clickedItem.PrimaryKeyColumn ?? "", clickedItem.PrimaryKeyValue);
                                dgvCascadeDetail.Columns.Clear();
                                dgvCascadeDetail.Rows.Clear();
                                dgvCascadeDetail.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                                dgvCascadeDetail.Columns.Add("Col", "Column");
                                dgvCascadeDetail.Columns.Add("Val", "Value");
                                dgvCascadeDetail.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                                dgvCascadeDetail.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                                dgvCascadeDetail.Columns[0].ReadOnly = true;
                                dgvCascadeDetail.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                                dgvCascadeDetail.Columns[0].DefaultCellStyle.ForeColor = Color.FromArgb(60, 100, 160);
                                if (row.Count > 0)
                                {
                                    foreach (var kvp in row) dgvCascadeDetail.Rows.Add(kvp.Key, kvp.Value);
                                    dgvCascadeDetail.Visible = true;
                                    lblCascadeHint.Visible = false;
                                }
                                else lblCascadeHint.Text = "(No data found)";
                            }
                            else 
                            {
                                lblRightTitle.Visible = false;
                                lblCascadeHint.Text = "Select a record or table on the left to view data.";
                            }
                        }
                        catch (Exception ex)
                        {
                            lblRightTitle.Visible = false;
                            lblCascadeHint.Text = $"❌ Error: {ex.Message}";
                        }
                    }
                };
            };
        }

        private void BuildCascadeTree(TreeView tv, JunkItem root)
        {
            tv.Nodes.Clear();
            var rootNode = new TreeNode($"🗃  {root.SchemaName}.{root.ObjectName}")
            {
                ForeColor = Color.FromArgb(200, 80, 80),
                NodeFont = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ToolTipText = $"PK: {root.PrimaryKeyColumn} = {root.PrimaryKeyValue}"
            };

            foreach (var dep in root.DependentObjects)
                AddCascadeNode(rootNode, dep, 1);

            tv.Nodes.Add(rootNode);
        }

        private void AddCascadeNode(TreeNode parent, JunkItem dep, int level)
        {
            bool isRowDetail = !string.IsNullOrEmpty(dep.PrimaryKeyValue);
            
            if (isRowDetail)
            {
                // To keep the TreeView clean, we no longer add Row Detail nodes.
                // We just pass their children (grandchild tables) up to the current parent.
                foreach (var child in dep.DependentObjects)
                {
                    // The level remains the same since we bypassed a node visually.
                    AddCascadeNode(parent, child, level);
                }
                return;
            }

            // Extract row count and foreign key column from DetectedContent
            var countMatch = System.Text.RegularExpressions.Regex.Match(dep.DetectedContent ?? "", @"(\d+)\s+rows?");
            string countStr = countMatch.Success ? $"{countMatch.Groups[1].Value} row(s)" : "";

            var fkColMatch = System.Text.RegularExpressions.Regex.Match(dep.DetectedContent ?? "", @"via\s+([a-zA-Z0-9_]+)");
            string fkStr = fkColMatch.Success ? $"via {fkColMatch.Groups[1].Value}" : "";

            string extraInfo = "";
            if (!string.IsNullOrEmpty(countStr) || !string.IsNullOrEmpty(fkStr))
            {
                extraInfo = $" ({countStr}{(string.IsNullOrEmpty(countStr) || string.IsNullOrEmpty(fkStr) ? "" : " ")}{fkStr})";
            }

            string icon = level == 1 ? "⚠" : "  └─";
            string nodeText = $"{icon}  {dep.SchemaName}.{dep.ObjectName}{extraInfo}";

            // To avoid duplicate table nodes under the same parent (from skipping multiple row nodes), check if already exists
            TreeNode? node = null;
            foreach (TreeNode n in parent.Nodes)
            {
                if (n.Text == nodeText)
                {
                    node = n;
                    break;
                }
            }

            if (node == null)
            {
                node = new TreeNode(nodeText)
                {
                    ForeColor = level == 1 ? Color.DarkOrange : Color.FromArgb(150, 100, 30),
                    ToolTipText = dep.DetectedContent ?? "",
                    Tag = dep // Save the item context!
                };
                
                // Only add the descriptive node if we couldn't parse out the FK info nicely
                if (!System.Text.RegularExpressions.Regex.IsMatch(dep.DetectedContent ?? "", @"via\s+[a-zA-Z0-9_]+") && !string.IsNullOrEmpty(dep.DetectedContent))
                {
                    node.Nodes.Add(new TreeNode($"   📌 {dep.DetectedContent}")
                    {
                        ForeColor = Color.Gray,
                        NodeFont = new Font("Segoe UI", 8.5f, FontStyle.Italic)
                    });
                }
                parent.Nodes.Add(node);
            }

            foreach (var child in dep.DependentObjects)
                AddCascadeNode(node, child, level + 1);
        }

        private string GenerateDeleteScript(JunkItem item)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("-- ================================================================");
            sb.AppendLine($"-- Junk Cleanup Script — Generated {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"-- Record: {item.SchemaName}.{item.ObjectName}");
            if (item.Type == JunkType.DataRecord)
                sb.AppendLine($"-- PK: {item.PrimaryKeyColumn} = '{item.PrimaryKeyValue}'");
            sb.AppendLine("-- ================================================================");
            sb.AppendLine();

            if (item.DependentObjects.Any())
            {
                sb.AppendLine("-- ⚠ CASCADE IMPACT: The following child records will also be deleted.");
                sb.AppendLine("-- Review carefully before executing!");
                sb.AppendLine();
                AppendCascadeDeleteScript(sb, item, 0);
            }

            sb.AppendLine("BEGIN;");
            sb.AppendLine();

            // Child deletes first (in reverse cascade order)
            if (item.DependentObjects.Any())
            {
                sb.AppendLine("-- Step 1: Delete child records (cascade order)");
                foreach (var dep in item.DependentObjects)
                {
                    sb.AppendLine($"--   Affects: {dep.SchemaName}.{dep.ObjectName} — {dep.DetectedContent}");
                }
                sb.AppendLine();
            }

            // Main delete
            if (item.Type == JunkType.DataRecord)
            {
                sb.AppendLine($"-- Step {(item.DependentObjects.Any() ? 2 : 1)}: Delete the junk record");
                sb.AppendLine($"DELETE FROM \"{item.SchemaName}\".\"{item.ObjectName}\"");
                sb.AppendLine($"  WHERE \"{item.PrimaryKeyColumn}\" = '{item.PrimaryKeyValue}';");
            }
            else
            {
                sb.AppendLine($"-- Drop object");
                sb.AppendLine(item.Type switch
                {
                    JunkType.Table  => $"DROP TABLE IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;",
                    JunkType.View   => $"DROP VIEW IF EXISTS \"{item.SchemaName}\".\"{item.ObjectName}\" CASCADE;",
                    JunkType.Column => $"ALTER TABLE \"{item.SchemaName}\".\"{item.ParentObjectName}\" DROP COLUMN IF EXISTS \"{item.ObjectName}\" CASCADE;",
                    _               => $"-- DROP {item.Type}: \"{item.SchemaName}\".\"{item.ObjectName}\""
                });
            }

            sb.AppendLine();
            sb.AppendLine("COMMIT;");
            return sb.ToString();
        }

        private void AppendCascadeDeleteScript(System.Text.StringBuilder sb, JunkItem item, int level)
        {
            string indent = new string(' ', level * 2);
            foreach (var dep in item.DependentObjects)
            {
                // Skip listing individual row details to avoid spamming the script with redundant warnings
                if (dep.DetectedContent != null && dep.DetectedContent.StartsWith("[ROW DETAIL]"))
                    continue;

                sb.AppendLine($"{indent}-- ⚠ {dep.DetectedContent}");
                
                // Extra metadata parsing: Assume DetectedContent has "reference this record via FK"
                var fkColMatch = System.Text.RegularExpressions.Regex.Match(dep.DetectedContent ?? "", @"via\s+([a-zA-Z0-9_]+)");
                if (fkColMatch.Success && !string.IsNullOrEmpty(item.PrimaryKeyValue))
                {
                    string fkCol = fkColMatch.Groups[1].Value;
                    sb.AppendLine($"{indent}DELETE FROM \"{dep.SchemaName}\".\"{dep.ObjectName}\" WHERE \"{fkCol}\" = '{item.PrimaryKeyValue}';");
                }
                else
                {
                     sb.AppendLine($"{indent}-- (Cannot determine FK column for auto-delete, please delete manually or let DB ON DELETE CASCADE handle it)");
                }

                AppendCascadeDeleteScript(sb, dep, level + 1);
            }
        }

        private void HighlightKeywordsInRange(RichTextBox rtb, int offset, int length, System.Collections.Generic.List<string>? keywords)
        {
            if (keywords == null) return;
            string valueText = rtb.Text.Substring(offset, Math.Min(length, rtb.Text.Length - offset));
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                int idx = 0;
                while ((idx = valueText.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    rtb.Select(offset + idx, kw.Length);
                    rtb.SelectionBackColor = Color.Yellow;
                    rtb.SelectionColor = Color.DarkRed;
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                    idx += kw.Length;
                }
            }
            rtb.DeselectAll();
        }

        private void HighlightKeywords(RichTextBox rtb, string text, System.Collections.Generic.List<string>? keywords)
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
                    rtb.SelectionColor = Color.DarkRed;
                    rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                    index += kw.Length;
                }
            }
            rtb.DeselectAll();
        }
    }
}
