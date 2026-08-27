using NetworkAdminTool.Interfaces;

namespace NetworkAdminTool.Forms
{
    /// <summary>
    /// Form giám sát CPU/RAM/Disk theo thời gian thực. Chỉ lo hiển thị,
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
        private Label? lblDiskTitle;
        private Label? lblDiskValue;
        private ProgressBar? prgCpu;
        private ProgressBar? prgRam;
        private ProgressBar? prgDisk;

        public MonitorForm(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            Text = "Thông tin hệ thống";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            MinimumSize = new Size(500, 360);
            BackColor = Color.FromArgb(244, 248, 253);
            Font = new Font("Segoe UI", 10F);

            InitializeComponents();
            InitializeTimer();

            Shown += (_, _) => UpdateMetrics();
            FormClosed += (s, e) => _timer?.Stop();
        }

        private void InitializeComponents()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                BackColor = Color.FromArgb(244, 248, 253),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));

            root.Controls.Add(new Label
            {
                Text = "Giám sát hệ thống",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
                ForeColor = Color.FromArgb(12, 28, 83),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            lblCpuTitle = new Label();
            lblCpuValue = new Label();
            prgCpu = new ProgressBar();
            lblRamTitle = new Label();
            lblRamValue = new Label();
            prgRam = new ProgressBar();
            lblDiskTitle = new Label();
            lblDiskValue = new Label();
            prgDisk = new ProgressBar();

            root.Controls.Add(BuildMetricPanel("CPU Usage", lblCpuTitle, lblCpuValue, prgCpu), 0, 1);
            root.Controls.Add(BuildMetricPanel("RAM Usage", lblRamTitle, lblRamValue, prgRam), 0, 2);
            root.Controls.Add(BuildMetricPanel("Disk Usage", lblDiskTitle, lblDiskValue, prgDisk), 0, 3);
            Controls.Add(root);
        }

        private static Control BuildMetricPanel(string title, Label titleLabel, Label valueLabel, ProgressBar progressBar)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(18),
                Margin = new Padding(0, 0, 0, 14)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            titleLabel.Text = title;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.ForeColor = Color.FromArgb(58, 72, 118);
            titleLabel.Font = new Font("Segoe UI", 11F);

            valueLabel.Text = "0.0 %";
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel.ForeColor = Color.FromArgb(12, 28, 83);
            valueLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);

            progressBar.Dock = DockStyle.Top;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Height = 18;
            progressBar.Margin = new Padding(0, 16, 0, 0);

            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(valueLabel, 1, 0);
            panel.Controls.Add(progressBar, 0, 1);
            panel.SetColumnSpan(progressBar, 2);
            return panel;
        }

        /// <summary>
        /// Cập nhật CPU/RAM/Disk mỗi 1 giây bằng Timer — không dùng vòng lặp
        /// while thủ công, tránh treo giao diện.
        /// </summary>
        private void InitializeTimer()
        {
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => UpdateMetrics();
            _timer.Start();
        }

        private void UpdateMetrics()
        {
            var stats = _monitorService.GetSystemStats();
            var diskUsage = _monitorService.GetDiskUsage();

            lblCpuValue!.Text = $"{stats.CpuUsagePercent:0.0} %";
            lblRamValue!.Text = $"{stats.RamUsagePercent:0.0} %";
            lblDiskValue!.Text = $"{diskUsage:0.0} %";
            prgCpu!.Value = ToProgressValue(stats.CpuUsagePercent);
            prgRam!.Value = ToProgressValue(stats.RamUsagePercent);
            prgDisk!.Value = ToProgressValue(diskUsage);
        }

        private static int ToProgressValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;

            return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
        }
    }
}
