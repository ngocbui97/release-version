using System;
using System.Drawing;
using System.Windows.Forms;
using ReleasePrepTool.Models;

namespace ReleasePrepTool.UI
{
    public class ConnectionDialog : Form
    {
        private TextBox txtHost = null!, txtPort = null!, txtUser = null!, txtPassword = null!, txtDbName = null!;
        public DatabaseConfig? Config { get; private set; }

        public ConnectionDialog(string title, DatabaseConfig? initialConfig = null)
        {
            this.Text = title;
            this.Size = new Size(480, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            // --- HEADER BANNER ---
            var pnlHeader = new Panel {
                Dock = DockStyle.Top, Height = 56, BackColor = UIConstants.Primary,
                Padding = new Padding(20, 14, 20, 0)
            };
            var lblTitle = new Label {
                Text = title, Dock = DockStyle.Fill,
                Font = new Font(UIConstants.MainFontName, 11f, FontStyle.Bold),
                ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft
            };
            pnlHeader.Controls.Add(lblTitle);

            // --- FORM BODY ---
            var pnlBody = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
                Padding = new Padding(24, 16, 24, 8), BackColor = Color.White
            };
            pnlBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            pnlBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            
            for (int i = 0; i < 5; i++)
                pnlBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            pnlBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Filler row to absorb extra space

            txtHost     = AddBodyRow(pnlBody, "Host:", initialConfig?.Host ?? "localhost", 0);
            txtPort     = AddBodyRow(pnlBody, "Port:", initialConfig?.Port.ToString() ?? "5432", 1);
            txtUser     = AddBodyRow(pnlBody, "Username:", initialConfig?.Username ?? "postgres", 2);
            txtPassword = AddBodyRow(pnlBody, "Password:", initialConfig?.Password ?? "123456", 3, true);
            txtDbName   = AddBodyRow(pnlBody, "Database Name:", initialConfig?.DatabaseName ?? "", 4);

            // --- FOOTER: Buttons ---
            var pnlFooter = new Panel {
                Dock = DockStyle.Bottom, Height = 64,
                BackColor = UIConstants.Surface,
                Padding = new Padding(20, 12, 20, 12)
            };
            // Separator line on top of footer
            pnlFooter.Paint += (s, e) => {
                using var pen = new Pen(UIConstants.Border, 1);
                e.Graphics.DrawLine(pen, 0, 0, pnlFooter.Width, 0);
            };

            var pnlBtns = new FlowLayoutPanel {
                Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight
            };

            var btnTest   = new Button { Text = "Test Connection", Width = 130, Height = 38, Margin = new Padding(0, 0, 8, 0) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 38, Margin = new Padding(0, 0, 8, 0) };
            var btnOk     = new Button { Text = "Connect", DialogResult = DialogResult.OK, Width = 100, Height = 38 };

            StyleSecondary(btnTest);
            StyleSecondary(btnCancel);
            StylePrimary(btnOk);

            btnTest.Click += async (object? s, EventArgs e) => {
                if (s == null) return;
                var btn = (Button)s;
                var originalText = btn.Text;
                btn.Text = "Testing...";
                btn.Enabled = false;
                try {
                    var connStr = $"Host={txtHost.Text};Port={(int.TryParse(txtPort.Text, out int p) ? p : 5432)};Username={txtUser.Text};Password={txtPassword.Text};Database={txtDbName.Text}";
                    using var conn = new Npgsql.NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                    btn.BackColor = UIConstants.Success;
                    btn.Text = "✔  Connected";
                    if (Config == null) Config = new DatabaseConfig { Host = "", Username = "", Password = "", DatabaseName = "" };
                    Config.IsValid = true;
                }
                catch (Exception ex) {
                    MessageBox.Show($"Connection failed:\n{ex.Message}", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (Config != null) Config.IsValid = false;
                    btn.Text = originalText;
                    btn.BackColor = UIConstants.Surface;
                }
                finally {
                    btn.Enabled = true;
                }
            };

            btnOk.Click += (object? s, EventArgs e) => {
                if (string.IsNullOrWhiteSpace(txtDbName.Text)) {
                    MessageBox.Show("Database name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
                var wasValid = Config?.IsValid ?? false;
                Config = new DatabaseConfig {
                    Host = txtHost.Text.Trim(),
                    Port = int.TryParse(txtPort.Text, out int p2) ? p2 : 5432,
                    Username = txtUser.Text.Trim(),
                    Password = txtPassword.Text,
                    DatabaseName = txtDbName.Text.Trim(),
                    IsValid = wasValid
                };
            };

            EventHandler markInvalid = (object? s, EventArgs e) => { if (Config != null) Config.IsValid = false; };
            txtHost.TextChanged     += markInvalid;
            txtPort.TextChanged     += markInvalid;
            txtUser.TextChanged     += markInvalid;
            txtPassword.TextChanged += markInvalid;
            txtDbName.TextChanged   += markInvalid;

            pnlBtns.Controls.AddRange(new Control[] { btnTest, btnCancel, btnOk });
            pnlFooter.Controls.Add(pnlBtns);

            this.Controls.Add(pnlHeader);   // Top
            this.Controls.Add(pnlFooter);   // Bottom
            this.Controls.Add(pnlBody);     // Fill added LAST to take remaining space

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private TextBox AddBodyRow(TableLayoutPanel grid, string labelText, string defaultValue, int row, bool isPassword = false)
        {
            var lbl = new Label {
                Text = labelText, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopRight, // Use Top alignment to match textbox margin
                Font = new Font(UIConstants.MainFontName, 9f),
                ForeColor = UIConstants.TextSecondary,
                Padding = new Padding(0, 16, 10, 0) // Align with textbox margin top (12) + small offset
            };
            var txt = new TextBox {
                Text = defaultValue, Dock = DockStyle.Top,
                UseSystemPasswordChar = isPassword,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(UIConstants.MainFontName, 9.5f),
                Margin = new Padding(0, 12, 0, 0),
                Height = 30
            };
            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(txt, 1, row);
            return txt;
        }

        private static void StylePrimary(Button btn)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = UIConstants.Primary;
            btn.ForeColor = Color.White;
            btn.Font = new Font(UIConstants.MainFontName, 9f, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
        }

        private static void StyleSecondary(Button btn)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = UIConstants.Border;
            btn.BackColor = UIConstants.Surface;
            btn.ForeColor = UIConstants.TextPrimary;
            btn.Font = new Font(UIConstants.MainFontName, 9f);
            btn.Cursor = Cursors.Hand;
        }
    }
}
