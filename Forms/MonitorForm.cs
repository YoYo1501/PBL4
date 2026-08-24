using NetworkAdminTool.Interfaces;

namespace NetworkAdminTool.Forms
{
    /// <summary>
    /// Form giám sát CPU/RAM theo thời gian thực. Chỉ lo hiển thị,
    /// logic thật nằm ở SystemMonitorService.
    /// </summary>
    public class MonitorForm : Form
    {
        private readonly ISystemMonitorService _monitorService;
        private System.Windows.Forms.Timer? _timer;

        private Label? lblCpuTitle;
        private Label? lblCpuValue;
        private Label? lblRamTitle;
        private Label? lblRamValue;

        public MonitorForm(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            Text = "Thông tin hệ thống";
            Width = 350;
            Height = 200;

            InitializeComponents();
            InitializeTimer();

            FormClosed += (s, e) => _timer?.Stop();
        }

        private void InitializeComponents()
        {
            lblCpuTitle = new Label { Text = "CPU Usage:", Left = 20, Top = 30, Width = 100 };
            lblCpuValue = new Label { Text = "0 %", Left = 130, Top = 30, Width = 150 };

            lblRamTitle = new Label { Text = "RAM Usage:", Left = 20, Top = 70, Width = 100 };
            lblRamValue = new Label { Text = "0 %", Left = 130, Top = 70, Width = 150 };

            Controls.Add(lblCpuTitle);
            Controls.Add(lblCpuValue);
            Controls.Add(lblRamTitle);
            Controls.Add(lblRamValue);
        }

        /// <summary>
        /// Cập nhật CPU/RAM mỗi 1 giây bằng Timer — không dùng vòng lặp
        /// while thủ công, tránh treo giao diện.
        /// </summary>
        private void InitializeTimer()
        {
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (s, e) =>
            {
                var stats = _monitorService.GetSystemStats();
                lblCpuValue!.Text = $"{stats.CpuUsagePercent:0.0} %";
                lblRamValue!.Text = $"{stats.RamUsagePercent:0.0} %";
            };
            _timer.Start();
        }
    }
}
