using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Forms
{
    /// <summary>
    /// Form ping một host cụ thể. Chỉ lo hiển thị,
    /// logic thật nằm ở PingService.
    /// </summary>
    public class PingForm : Form
    {
        private readonly IPingService _pingService;
        private readonly ILoggerService _logger;

        private Label? lblHost;
        private TextBox? txtHost;
        private Button? btnPing;
        private Label? lblResult;
        private Label? lblStatus;

        private static readonly Color PageBack = Color.FromArgb(244, 248, 253);
        private static readonly Color PanelBack = Color.White;
        private static readonly Color PanelBack2 = Color.White;
        private static readonly Color Border = Color.FromArgb(213, 224, 240);
        private static readonly Color Ink = Color.FromArgb(12, 28, 83);
        private static readonly Color Muted = Color.FromArgb(58, 72, 118);
        private static readonly Color Blue = Color.FromArgb(13, 101, 238);

        public PingForm(IPingService pingService, ILoggerService logger)
        {
            _pingService = pingService;
            _logger = logger;
            Text = "Ping";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 420);
            MinimumSize = new Size(560, 360);
            BackColor = PageBack;
            Font = new Font("Segoe UI", 10F);

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                BackColor = PageBack,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2
            };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            header.Controls.Add(new Label
            {
                Text = "Ping Tool",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold),
                ForeColor = Ink,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "Kiem tra do tre va trang thai ket noi cua mot host",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11.5F),
                ForeColor = Muted,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            var inputLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));

            lblHost = new Label
            {
                Text = "IP / Hostname",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
            };
            txtHost = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 13F),
                PlaceholderText = "192.168.1.1 hoặc google.com",
                Margin = new Padding(0, 18, 14, 0),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Ink,
                Height = 38
            };
            btnPing = new Button
            {
                Text = "Ping",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 14, 0, 14),
                BackColor = Blue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPing.FlatAppearance.BorderSize = 0;
            inputLayout.Controls.Add(lblHost, 0, 0);
            inputLayout.Controls.Add(txtHost, 1, 0);
            inputLayout.Controls.Add(btnPing, 2, 0);

            lblStatus = new Label
            {
                Text = "San sang ping",
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                Font = new Font("Segoe UI", 10.5F),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblResult = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                BackColor = PanelBack2,
                ForeColor = Ink,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 12F),
                Text = "Nhập host rồi bấm Ping để kiểm tra độ trễ."
            };

            btnPing.Click += BtnPing_Click;

            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelBack,
                BorderColor = Border,
                Radius = 16,
                Padding = new Padding(20)
            };
            card.Controls.Add(lblResult);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(inputLayout, 0, 1);
            root.Controls.Add(lblStatus, 0, 2);
            root.Controls.Add(card, 0, 3);
            Controls.Add(root);
        }

        /// <summary>
        /// Chạy nền để không đứng UI trong lúc chờ phản hồi ping.
        /// </summary>
        private async void BtnPing_Click(object? sender, EventArgs e)
        {
            var host = txtHost?.Text.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(host))
            {
                MessageBox.Show("Vui lòng nhập IP hoặc hostname.");
                return;
            }

            btnPing!.Enabled = false;
            lblStatus!.Text = $"Dang ping {host}...";
            lblResult!.Text = "Dang gui goi ICMP va cho phan hoi...";

            try
            {
                PingResult result = await Task.Run(() => _pingService.Ping(host, timeoutMs: 1000, retryCount: 1));

                lblResult.Text = result.Success
                    ? $"Thành công!\nThời gian phản hồi: {result.RoundtripTimeMs} ms\nTrạng thái: {result.StatusMessage}"
                    : $"Thất bại.\nTrạng thái: {result.StatusMessage}";
                lblStatus.Text = result.Success ? "Host phan hoi thanh cong" : "Host khong phan hoi";

                _logger.Log($"Ping {host}: {result.StatusMessage}");
            }
            finally
            {
                btnPing.Enabled = true;
            }
        }
    }
}
