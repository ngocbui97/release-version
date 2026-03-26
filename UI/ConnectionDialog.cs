using System;
using System.Drawing;
using System.Windows.Forms;
using ReleasePrepTool.Models;

namespace ReleasePrepTool.UI
{
    public class ConnectionDialog : Form
    {
        private TextBox txtHost, txtPort, txtUser, txtPassword, txtDbName;
        public DatabaseConfig Config { get; private set; }

        public ConnectionDialog(string title, DatabaseConfig initialConfig = null)
        {
            this.Text = title;
            this.Size = new Size(400, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20) };

            txtHost = CreateInputRow(panel, "Host:", initialConfig?.Host ?? "localhost");
            txtPort = CreateInputRow(panel, "Port:", initialConfig?.Port.ToString() ?? "5432");
            txtUser = CreateInputRow(panel, "Username:", initialConfig?.Username ?? "postgres");
            txtPassword = CreateInputRow(panel, "Password:", initialConfig?.Password ?? "123456", true);
            txtDbName = CreateInputRow(panel, "Database Name:", initialConfig?.DatabaseName ?? "");

            var pnlBtns = new FlowLayoutPanel { Width = 350, Height = 40, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 20, 0, 0) };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
            var btnTest = new Button { Text = "Test", Width = 80 };
            
            btnTest.Click += async (s, e) => {
                var btn = (Button)s;
                var originalText = btn.Text;
                btn.Text = "Testing...";
                btn.Enabled = false;
                try {
                    var connStr = $"Host={txtHost.Text};Port={(int.TryParse(txtPort.Text, out int p) ? p : 5432)};Username={txtUser.Text};Password={txtPassword.Text};Database={txtDbName.Text}";
                    using var conn = new Npgsql.NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                    MessageBox.Show("Connection successful!", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    if (Config == null) Config = new DatabaseConfig();
                    Config.IsValid = true;
                }
                catch (Exception ex) {
                    MessageBox.Show($"Connection failed:\n{ex.Message}", "Test Connection", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (Config != null) Config.IsValid = false;
                }
                finally {
                    btn.Text = originalText;
                    btn.Enabled = true;
                }
            };

            btnOk.Click += (s, e) => {
                if (string.IsNullOrWhiteSpace(txtDbName.Text)) {
                    MessageBox.Show("Database name is required.");
                    this.DialogResult = DialogResult.None;
                    return;
                }
                var wasValid = Config?.IsValid ?? false;
                Config = new DatabaseConfig {
                    Host = txtHost.Text,
                    Port = int.TryParse(txtPort.Text, out int p2) ? p2 : 5432,
                    Username = txtUser.Text,
                    Password = txtPassword.Text,
                    DatabaseName = txtDbName.Text,
                    IsValid = wasValid
                };
            };
            
            EventHandler markInvalid = (s, e) => { if (Config != null) Config.IsValid = false; };
            txtHost.TextChanged += markInvalid;
            txtPort.TextChanged += markInvalid;
            txtUser.TextChanged += markInvalid;
            txtPassword.TextChanged += markInvalid;
            txtDbName.TextChanged += markInvalid;

            pnlBtns.Controls.Add(btnCancel);
            pnlBtns.Controls.Add(btnOk);
            pnlBtns.Controls.Add(btnTest);
            panel.Controls.Add(pnlBtns);

            this.Controls.Add(panel);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private TextBox CreateInputRow(FlowLayoutPanel parent, string labelText, string defaultValue, bool isPassword = false)
        {
            var pnl = new FlowLayoutPanel { Width = 350, Height = 30 };
            var lbl = new Label { Text = labelText, Width = 100, TextAlign = ContentAlignment.MiddleRight };
            var txt = new TextBox { Text = defaultValue, Width = 230, UseSystemPasswordChar = isPassword };
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(txt);
            parent.Controls.Add(pnl);
            return txt;
        }
    }
}
