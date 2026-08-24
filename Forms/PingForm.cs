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

        public PingForm(IPingService pingService, ILoggerService logger)
        {
            _pingService = pingService;
            _logger = logger;
            Text = "Ping";
            Width = 420;
            Height = 220;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            lblHost = new Label { Text = "IP / Hostname:", Left = 20, Top = 20, Width = 100 };
            txtHost = new TextBox { Left = 130, Top = 18, Width = 180 };
            btnPing = new Button { Text = "Ping", Left = 320, Top = 16, Width = 70 };
            lblResult = new Label { Left = 20, Top = 60, Width = 370, Height = 100, Text = "" };

            btnPing.Click += BtnPing_Click;

            Controls.Add(lblHost);
            Controls.Add(txtHost);
            Controls.Add(btnPing);
            Controls.Add(lblResult);
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
            lblResult!.Text = "Đang ping...";

            PingResult result = await Task.Run(() => _pingService.Ping(host, timeoutMs: 1000, retryCount: 1));

            lblResult.Text = result.Success
                ? $"Thành công!\nThời gian phản hồi: {result.RoundtripTimeMs} ms\nTrạng thái: {result.StatusMessage}"
                : $"Thất bại.\nTrạng thái: {result.StatusMessage}";

            _logger.Log($"Ping {host}: {result.StatusMessage}");
            btnPing.Enabled = true;
        }
    }
}
