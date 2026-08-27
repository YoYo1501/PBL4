using Microsoft.Extensions.Configuration;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Services;

namespace NetworkAdminTool.Forms
{
    public class NetworkInfoForm : Form
    {
        private readonly INetworkInfoService _networkInfoService;
        private readonly DataGridView _grid;
        private readonly Label _summary;

        public NetworkInfoForm(INetworkInfoService networkInfoService)
        {
            _networkInfoService = networkInfoService;
            FeatureFormStyle.ApplyWindow(this, "Network Info", new Size(920, 560), new Size(720, 420));

            _summary = FeatureFormStyle.BuildSubtitle("Đang tải thông tin adapter...");
            _grid = FeatureFormStyle.BuildGrid();
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Adapter", DataPropertyName = "Name", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mô tả", DataPropertyName = "Description", FillWeight = 36 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "IP Address", DataPropertyName = "IpAddress", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Subnet", DataPropertyName = "Subnet", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gateway", DataPropertyName = "Gateway", FillWeight = 18 });

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(_summary, 0, 0);
            body.Controls.Add(_grid, 0, 1);

            Controls.Add(FeatureFormStyle.BuildShell("Network Info", "Thông tin các card mạng đang hoạt động", body));
            Load += (_, _) => LoadInterfaces();
            Activated += (_, _) => LoadInterfaces();
        }

        private void LoadInterfaces()
        {
            var rows = _networkInfoService.GetAvailableNetworkInterfaces()
                .Select(item => new
                {
                    item.Name,
                    item.Description,
                    item.IpAddress,
                    Subnet = _networkInfoService.GetSubnetPrefix(item.IpAddress),
                    Gateway = string.IsNullOrWhiteSpace(item.Gateway) ? "-" : item.Gateway
                })
                .ToList();

            _grid.DataSource = rows;
            _summary.Text = rows.Count == 0
                ? "Không tìm thấy adapter đang hoạt động."
                : $"Tìm thấy {rows.Count} adapter đang hoạt động.";
        }
    }

    public class AlertsForm : Form
    {
        private readonly NetworkDashboardState _dashboardState;
        private readonly ListView _list;

        public AlertsForm(NetworkDashboardState dashboardState)
        {
            _dashboardState = dashboardState;
            FeatureFormStyle.ApplyWindow(this, "Alerts", new Size(760, 520), new Size(620, 420));
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                ForeColor = FeatureFormStyle.Ink,
                BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Mức độ", 110);
            _list.Columns.Add("Nội dung", 390);
            _list.Columns.Add("Chi tiết", 180);
            _list.Columns.Add("Thời gian", 140);

            Controls.Add(FeatureFormStyle.BuildShell("Alerts", "Theo dõi các sự kiện mạng gần đây", _list));
            Load += (_, _) => LoadAlerts();
        }

        private void LoadAlerts()
        {
            _list.Items.Clear();
            var alerts = _dashboardState.GetRecentAlerts(50);
            foreach (var alert in alerts)
            {
                _list.Items.Add(new ListViewItem(new[]
                {
                    alert.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "Cảnh báo" : "Thông tin",
                    alert.Title,
                    alert.Message,
                    alert.CreatedAt.ToString("HH:mm:ss")
                }));
            }

            if (_list.Items.Count == 0)
            {
                _list.Items.Add(new ListViewItem(new[] { "Thông tin", "Chưa có cảnh báo", "Hãy chạy IP Scanner để cập nhật", "-" }));
            }

            _dashboardState.MarkAlertsSeen();
        }
    }

    public class LogsForm : Form
    {
        private readonly ListBox _files;
        private readonly TextBox _content;

        public LogsForm()
        {
            FeatureFormStyle.ApplyWindow(this, "Logs", new Size(920, 560), new Size(720, 420));
            _files = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.White,
                ForeColor = FeatureFormStyle.Ink
            };
            _content = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10F),
                BackColor = Color.White,
                ForeColor = FeatureFormStyle.Ink
            };
            _files.SelectedIndexChanged += (_, _) => LoadSelectedLog();

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.Controls.Add(FeatureFormStyle.WrapPanel(_files, new Padding(0, 0, 14, 0)), 0, 0);
            body.Controls.Add(FeatureFormStyle.WrapPanel(_content, Padding.Empty), 1, 0);

            Controls.Add(FeatureFormStyle.BuildShell("Logs", "Đọc log trong thư mục Logs", body));
            Load += (_, _) => LoadLogs();
        }

        private void LoadLogs()
        {
            _files.Items.Clear();
            var logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
            if (!Directory.Exists(logDir))
                logDir = Path.Combine(Environment.CurrentDirectory, "Logs");

            if (!Directory.Exists(logDir))
            {
                _content.Text = "Chưa có thư mục Logs.";
                return;
            }

            foreach (var file in Directory.GetFiles(logDir, "*.log").OrderByDescending(File.GetLastWriteTime))
                _files.Items.Add(file);

            if (_files.Items.Count > 0)
                _files.SelectedIndex = 0;
            else
                _content.Text = "Chưa có file log.";
        }

        private void LoadSelectedLog()
        {
            if (_files.SelectedItem is not string file)
                return;

            try
            {
                _content.Text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                _content.Text = $"Không đọc được log: {ex.Message}";
            }
        }
    }

    public class SettingsForm : Form
    {
        public SettingsForm(IConfiguration configuration)
        {
            FeatureFormStyle.ApplyWindow(this, "Settings", new Size(720, 500), new Size(620, 420));
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 0,
                BackColor = Color.Transparent
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddSetting(body, "Subnet mặc định", configuration["NetworkSettings:DefaultSubnet"] ?? "-");
            AddSetting(body, "Timeout Ping", $"{configuration["NetworkSettings:PingTimeoutMs"] ?? "-"} ms");
            AddSetting(body, "Timeout Scan", $"{configuration["NetworkSettings:ScanTimeoutMs"] ?? "-"} ms");
            AddSetting(body, "Số host tối đa", configuration["NetworkSettings:MaxHostsToScan"] ?? "-");
            AddSetting(body, "Thư mục log", configuration["Logging:LogFolder"] ?? "Logs");

            Controls.Add(FeatureFormStyle.BuildShell("Settings", "Cấu hình hiện tại của ứng dụng", FeatureFormStyle.WrapPanel(body, Padding.Empty)));
        }

        private static void AddSetting(TableLayoutPanel body, string name, string value)
        {
            var row = body.RowCount++;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            body.Controls.Add(FeatureFormStyle.BuildValueLabel(name, false), 0, row);
            body.Controls.Add(FeatureFormStyle.BuildValueLabel(value, true), 1, row);
        }
    }

    public class AboutForm : Form
    {
        public AboutForm()
        {
            FeatureFormStyle.ApplyWindow(this, "About", new Size(680, 460), new Size(600, 380));
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            body.Controls.Add(FeatureFormStyle.BuildValueLabel("Network Administration Tool", true, 18F), 0, 0);
            body.Controls.Add(FeatureFormStyle.BuildValueLabel(".NET WinForms - PBL4", false), 0, 1);
            body.Controls.Add(FeatureFormStyle.BuildValueLabel("Công cụ hỗ trợ quản trị mạng cục bộ: quét IP, ping, theo dõi CPU/RAM, xem thông tin adapter và log hoạt động.", false), 0, 2);
            body.Controls.Add(FeatureFormStyle.BuildValueLabel("Version 1.0.0", true), 0, 3);

            Controls.Add(FeatureFormStyle.BuildShell("About", "Thông tin ứng dụng", FeatureFormStyle.WrapPanel(body, Padding.Empty)));
        }
    }

    internal static class FeatureFormStyle
    {
        public static readonly Color PageBack = Color.FromArgb(244, 248, 253);
        public static readonly Color Ink = Color.FromArgb(12, 28, 83);
        public static readonly Color Muted = Color.FromArgb(58, 72, 118);
        private static readonly Color Blue = Color.FromArgb(13, 101, 238);

        public static void ApplyWindow(Form form, string title, Size clientSize, Size minimumSize)
        {
            form.Text = title;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = clientSize;
            form.MinimumSize = minimumSize;
            form.BackColor = PageBack;
            form.Font = new Font("Segoe UI", 10F);
        }

        public static Control BuildShell(string title, string subtitle, Control body)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                BackColor = PageBack,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            header.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
                ForeColor = Ink,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11F),
                ForeColor = Muted,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(WrapPanel(body, Padding.Empty), 0, 1);
            return root;
        }

        public static Control WrapPanel(Control child, Padding margin)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                Margin = margin,
                BackColor = Color.White
            };
            child.Dock = DockStyle.Fill;
            panel.Controls.Add(child);
            return panel;
        }

        public static Label BuildSubtitle(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Label BuildValueLabel(string text, bool strong, float size = 11F)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = strong ? Ink : Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(0, 0, 12, 0)
            };
        }

        public static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                GridColor = Color.FromArgb(226, 232, 242),
                EnableHeadersVisualStyles = false
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Blue;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 242, 255);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.ColumnHeadersHeight = 38;
            grid.RowTemplate.Height = 34;
            return grid;
        }
    }
}
