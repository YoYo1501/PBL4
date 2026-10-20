using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net.NetworkInformation;
using Microsoft.Extensions.DependencyInjection;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;
using NetworkAdminTool.Services;

namespace NetworkAdminTool.Forms
{
    public class MainForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer;
        private Point _dragStart;
        private bool _dragging;

        public MainForm(IServiceProvider serviceProvider)
        {
            Text = "Network Administration Tool";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1620, 968);
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(1200, 760);
            BackColor = Color.FromArgb(5, 13, 26);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            ShowIcon = false;
            WindowState = FormWindowState.Maximized;

            var dashboard = new DashboardCanvas(serviceProvider) { Dock = DockStyle.Fill };
            dashboard.DragTitle += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _dragging = true;
                    _dragStart = e.Location;
                }
            };
            dashboard.MoveTitle += (_, e) =>
            {
                if (_dragging)
                {
                    Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
                }
            };
            dashboard.ReleaseTitle += (_, _) => _dragging = false;
            dashboard.Minimize += (_, _) => WindowState = FormWindowState.Minimized;
            dashboard.Maximize += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            dashboard.CloseApp += (_, _) => Close();
            Controls.Add(dashboard);

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => dashboard.Invalidate();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosed(e);
        }
    }

    internal sealed class DashboardCanvas : Control
    {
        private const int TitleHeight = 48;
        private const int SidebarWidth = 280;

        private readonly IServiceProvider _services;
        private readonly INetworkInfoService? _networkInfoService;
        private readonly ISystemMonitorService? _monitorService;
        private readonly NetworkDashboardState? _dashboardState;
        private readonly Queue<float> _downloadMbpsHistory = new();
        private readonly Queue<float> _uploadMbpsHistory = new();
        private readonly List<HitArea> _hits = new();
        private long? _lastBytesReceived;
        private long? _lastBytesSent;
        private DateTime _lastTrafficSampleUtc = DateTime.MinValue;
        private string? _hoverKey;

        public event MouseEventHandler? DragTitle;
        public event MouseEventHandler? MoveTitle;
        public event MouseEventHandler? ReleaseTitle;
        public event EventHandler? Minimize;
        public event EventHandler? Maximize;
        public event EventHandler? CloseApp;

        public DashboardCanvas(IServiceProvider services)
        {
            _services = services;
            _networkInfoService = services.GetService<INetworkInfoService>();
            _monitorService = services.GetService<ISystemMonitorService>();
            _dashboardState = services.GetService<NetworkDashboardState>();
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            _hits.Clear();

            using (var bg = new LinearGradientBrush(ClientRectangle, C(5, 13, 27), C(8, 23, 41), LinearGradientMode.ForwardDiagonal))
                g.FillRectangle(bg, ClientRectangle);
            using (var frame = new Pen(C(20, 58, 90)))
                g.DrawRound(frame, new Rectangle(0, 0, Width - 1, Height - 1), 12);

            DrawTitle(g);
            DrawSidebar(g);
            DrawMain(g);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hit = _hits.FirstOrDefault(h => h.Bounds.Contains(e.Location));
            var hoverKey = hit.Bounds == Rectangle.Empty ? null : hit.Key;
            if (!string.Equals(hoverKey, _hoverKey, StringComparison.Ordinal))
            {
                _hoverKey = hoverKey;
                Invalidate();
            }
            Cursor = hit.Action != null ? Cursors.Hand : Cursors.Default;
            if (e.Y <= TitleHeight && e.X < Width - 190)
                MoveTitle?.Invoke(this, e);
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Y <= TitleHeight && e.X < Width - 190)
                DragTitle?.Invoke(this, e);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            ReleaseTitle?.Invoke(this, e);
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            var p = PointToClient(MousePosition);
            _hits.FirstOrDefault(h => h.Bounds.Contains(p)).Action?.Invoke();
            base.OnClick(e);
        }

        private void DrawTitle(Graphics g)
        {
            var r = new Rectangle(7, 0, Width - 14, TitleHeight);
            using (var b = new LinearGradientBrush(r, C(7, 18, 34), C(10, 23, 43), LinearGradientMode.Horizontal))
                g.FillRound(b, r, 9);
            using (var p = new Pen(C(19, 55, 80)))
                g.DrawRound(p, r, 9);

            DrawAtom(g, new Rectangle(21, 14, 22, 22), C(0, 188, 255));
            DrawText(g, "Network Administration Tool", 58, 15, 280, 24, 11.5f, FontStyle.Bold, Color.White);

            var min = new Rectangle(Width - 182, 10, 38, 28);
            var max = new Rectangle(Width - 118, 10, 38, 28);
            var close = new Rectangle(Width - 54, 10, 38, 28);
            Hit(min, () => Minimize?.Invoke(this, EventArgs.Empty), "minimize");
            Hit(max, () => Maximize?.Invoke(this, EventArgs.Empty), "maximize");
            Hit(close, () => CloseApp?.Invoke(this, EventArgs.Empty), "close");
            using var pen = new Pen(C(224, 230, 241), 1.6f);
            g.DrawLine(pen, min.X + 13, min.Y + 14, min.X + 25, min.Y + 14);
            g.DrawRectangle(pen, max.X + 13, max.Y + 8, 12, 12);
            g.DrawLine(pen, close.X + 12, close.Y + 8, close.X + 25, close.Y + 21);
            g.DrawLine(pen, close.X + 25, close.Y + 8, close.X + 12, close.Y + 21);
        }

        private void DrawSidebar(Graphics g)
        {
            var r = new Rectangle(8, TitleHeight, SidebarWidth - 10, Height - TitleHeight - 6);
            using (var b = new LinearGradientBrush(r, C(5, 17, 34), C(8, 25, 45), LinearGradientMode.Vertical))
                g.FillRound(b, r, 16);
            using (var p = new Pen(C(24, 59, 92)))
                g.DrawRound(p, r, 16);

            DrawLogo(g, new Rectangle(95, 73, 90, 68));
            DrawText(g, "NET ADMIN", 28, 143, 224, 31, 19f, FontStyle.Bold, Color.White, true);
            DrawText(g, "v1.0.0", 100, 176, 80, 18, 9.5f, FontStyle.Regular, C(164, 177, 201), true);

            var items = new[]
            {
                ("Dashboard", "home"), ("IP Scanner", "search"), ("Ping Tool", "move"), ("Monitoring", "bars"),
                ("Network Info", "globe"), ("Alerts", "bell"), ("Logs", "doc"), ("Settings", "gear"), ("About", "info")
            };
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var ir = new Rectangle(21, 200 + i * 46, 244, 47);
                if (i == 0)
                {
                    using var b = new LinearGradientBrush(ir, C(72, 100, 231), C(62, 58, 174), LinearGradientMode.Horizontal);
                    g.FillRound(b, ir, 9);
                }
                else if (string.Equals(_hoverKey, item.Item1, StringComparison.Ordinal))
                {
                    using var b = new SolidBrush(C(22, 47, 78));
                    g.FillRound(b, ir, 9);
                }
                DrawNavIcon(g, item.Item2, new Rectangle(ir.X + 13, ir.Y + 13, 21, 21), i == 0 ? Color.White : C(47, 145, 255));
                DrawText(g, item.Item1, ir.X + 45, ir.Y + 12, 145, 23, 12.5f, i == 0 ? FontStyle.Bold : FontStyle.Regular, Color.White);
                if (item.Item1 == "Alerts")
                {
                    var alertCount = _dashboardState?.GetUnreadAlertCount() ?? 0;
                    if (alertCount > 0)
                    {
                        var badgeText = alertCount > 99 ? "99+" : alertCount.ToString(CultureInfo.InvariantCulture);
                        var badgeWidth = alertCount > 9 ? 43 : 35;
                        var badge = new Rectangle(ir.Right - 22 - badgeWidth, ir.Y + 12, badgeWidth, 25);
                        using var b = new LinearGradientBrush(badge, C(150, 70, 247), C(102, 56, 219), LinearGradientMode.Horizontal);
                        g.FillRound(b, badge, 13);
                        DrawText(g, badgeText, badge.X, badge.Y + 2, badge.Width, 18, 10f, FontStyle.Bold, Color.White, true);
                    }
                }

                Action? action = item.Item1 switch
                {
                    "IP Scanner" => () => _services.GetRequiredService<ScannerForm>().Show(),
                    "Ping Tool" => () => _services.GetRequiredService<PingForm>().Show(),
                    "Monitoring" => () => _services.GetRequiredService<MonitorForm>().Show(),
                    "Network Info" => () => _services.GetRequiredService<NetworkInfoForm>().Show(),
                    "Alerts" => () => _services.GetRequiredService<AlertsForm>().Show(),
                    "Logs" => () => _services.GetRequiredService<LogsForm>().Show(),
                    "Settings" => () => _services.GetRequiredService<SettingsForm>().Show(),
                    "About" => () => _services.GetRequiredService<AboutForm>().Show(),
                    _ => null
                };
                Hit(ir, action, item.Item1);
            }

            DrawSystemCard(g, new Rectangle(23, Height - 332, 240, 297));
        }

        private void DrawMain(Graphics g)
        {
            var main = new Rectangle(SidebarWidth + 10, TitleHeight, Width - SidebarWidth - 18, Height - TitleHeight - 6);
            using (var b = new LinearGradientBrush(main, C(7, 20, 37), C(8, 24, 42), LinearGradientMode.ForwardDiagonal))
                g.FillRound(b, main, 16);
            using (var p = new Pen(C(24, 60, 96)))
                g.DrawRound(p, main, 16);

            var x = main.X + 38;
            var y = main.Y + 30;
            DrawText(g, "Xin ch\u00e0o, Administrator!", x, y, 540, 38, 23f, FontStyle.Bold, Color.White);
            DrawText(g, "T\u1ed5ng quan t\u00ecnh tr\u1ea1ng m\u1ea1ng v\u00e0 h\u1ec7 th\u1ed1ng", x, y + 42, 500, 24, 13.5f, FontStyle.Regular, C(219, 226, 240));
            DrawClock(g, new Rectangle(main.Right - 335, y + 2, 305, 70));

            var gap = 14;
            var cardW = Math.Max(178, (main.Width - 76 - gap * 4) / 5);
            var cardY = main.Y + 108;
            var dashboardDevices = GetDashboardDevices();
            var totalDevices = dashboardDevices.Length;
            var onlineDevices = dashboardDevices.Count(device => device.Online);
            var offlineDevices = totalDevices - onlineDevices;
            var pingValues = dashboardDevices
                .Where(device => device.Online && device.PingMs.HasValue)
                .Select(device => device.PingMs!.Value)
                .ToList();
            var averagePingText = pingValues.Count == 0 ? "-" : $"{pingValues.Average():0.#} ms";
            var stats = new[]
            {
                new Stat("T\u1ed5ng thi\u1ebft b\u1ecb", totalDevices.ToString(), "Theo bảng gần đây", C(17,160,255), "monitor"),
                new Stat("Online", onlineDevices.ToString(), PercentText(onlineDevices, totalDevices), C(37,207,92), "shield"),
                new Stat("Offline", offlineDevices.ToString(), PercentText(offlineDevices, totalDevices), C(249,42,100), "shieldx"),
                new Stat("Ping trung b\u00ecnh", averagePingText, "Theo thiết bị online", C(132,50,252), "pulse"),
                new Stat("Th\u1eddi gian ho\u1ea1t \u0111\u1ed9ng", FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64)), "Uptime hệ thống", C(242,142,10), "clock")
            };
            for (var i = 0; i < stats.Length; i++)
                DrawStat(g, new Rectangle(x + i * (cardW + gap), cardY, cardW, 120), stats[i]);

            var topY = cardY + 138;
            var chartW = (int)((main.Width - 76) * 0.53);
            var perfW = main.Width - 76 - chartW - 18;
            DrawChart(g, new Rectangle(x - 15, topY, chartW, 298));
            DrawPerformance(g, new Rectangle(x - 15 + chartW + 18, topY, perfW, 298));

            var bottomY = topY + 318;
            var bottomH = main.Bottom - bottomY - 18;
            var tableW = (int)((main.Width - 76) * 0.60);
            DrawTable(g, new Rectangle(x - 15, bottomY, tableW, bottomH), dashboardDevices);
            DrawAlerts(g, new Rectangle(x - 15 + tableW + 18, bottomY, main.Width - 76 - tableW - 18, bottomH));
        }

        private void DrawSystemCard(Graphics g, Rectangle r)
        {
            using (var b = new LinearGradientBrush(r, C(8, 28, 50), C(7, 21, 38), LinearGradientMode.Vertical))
                g.FillRound(b, r, 9);
            using (var p = new Pen(C(22, 56, 89)))
                g.DrawRound(p, r, 9);
            DrawText(g, "H\u1ec6 TH\u1ed0NG", r.X + 15, r.Y + 20, 110, 22, 13f, FontStyle.Regular, C(161, 176, 203));
            DrawWifi(g, new Rectangle(r.Right - 50, r.Y + 22, 28, 22), C(38, 225, 99));
            using (var dot = new SolidBrush(C(57, 232, 97)))
                g.FillEllipse(dot, r.X + 17, r.Y + 49, 11, 11);
            DrawText(g, "Online", r.X + 35, r.Y + 45, 80, 24, 11f, FontStyle.Regular, C(42, 234, 104));

            var adapter = _networkInfoService?.GetAvailableNetworkInterfaces().FirstOrDefault();
            var localIp = adapter?.IpAddress ?? string.Empty;
            var labels = new[] { "Adapter", "IP Address", "Gateway", "Subnet" };
            var values = new[]
            {
                string.IsNullOrWhiteSpace(adapter?.Name) ? "-" : adapter!.Name,
                string.IsNullOrWhiteSpace(localIp) ? "-" : localIp,
                string.IsNullOrWhiteSpace(adapter?.Gateway) ? "-" : adapter!.Gateway,
                string.IsNullOrWhiteSpace(localIp) ? "-" : _networkInfoService?.GetSubnetPrefix(localIp) ?? "-"
            };
            var yy = r.Y + 82;
            for (var i = 0; i < labels.Length; i++)
            {
                DrawText(g, labels[i], r.X + 15, yy, 150, 20, 12f, FontStyle.Regular, C(153, 169, 197));
                DrawText(g, values[i], r.X + 15, yy + 23, 170, 24, 15f, FontStyle.Regular, Color.White);
                yy += 54;
            }
        }

        private void DrawClock(Graphics g, Rectangle r)
        {
            DrawClockIcon(g, new Rectangle(r.X, r.Y + 10, 44, 44), C(184, 196, 224));
            var now = DateTime.Now;
            DrawText(g, now.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture), r.X + 58, r.Y + 3, 235, 31, 22f, FontStyle.Bold, Color.White);
            DrawText(g, now.ToString("dd/MM/yyyy - dddd", new CultureInfo("vi-VN")), r.X + 60, r.Y + 41, 225, 24, 11.5f, FontStyle.Regular, C(212, 219, 234));
        }

        private void DrawStat(Graphics g, Rectangle r, Stat s)
        {
            using (var b = new LinearGradientBrush(r, C(16, 39, 68), C(17, 31, 48), LinearGradientMode.ForwardDiagonal))
                g.FillRound(b, r, 13);
            using (var tint = new SolidBrush(Color.FromArgb(43, s.Accent)))
                g.FillRound(tint, r, 13);
            using (var p = new Pen(Color.FromArgb(120, s.Accent)))
                g.DrawRound(p, r, 13);

            var icon = new Rectangle(r.X + 18, r.Y + 24, 58, 58);
            using (var glow = new PathGradientBrush(icon.RoundPath(18)) { CenterColor = Color.FromArgb(235, s.Accent), SurroundColors = new[] { Color.FromArgb(30, s.Accent) } })
                g.FillRound(glow, icon, 18);
            DrawStatIcon(g, s.Icon, icon, Color.White);
            var textX = icon.Right + 18;
            var textW = Math.Max(72, r.Right - textX - 14);
            DrawText(g, s.Title, textX, r.Y + 24, textW, 22, 11.5f, FontStyle.Regular, C(226, 233, 246));
            DrawText(g, s.Value, textX, r.Y + 49, textW, 34, 25f, FontStyle.Bold, Color.White);
            DrawText(g, s.Subtitle, textX, r.Y + 86, textW, 22, 11.5f, FontStyle.Regular, C(210, 219, 236));
        }

        private void DrawChart(Graphics g, Rectangle r)
        {
            Panel(g, r, "L\u01afU L\u01af\u1ee2NG M\u1ea0NG");
            UpdateNetworkTrafficSample();
            var download = _downloadMbpsHistory.ToArray();
            var upload = _uploadMbpsHistory.ToArray();
            var currentDownload = download.Length == 0 ? 0 : download[^1];
            var currentUpload = upload.Length == 0 ? 0 : upload[^1];

            var pill = new Rectangle(r.Right - 118, r.Y + 17, 98, 32);
            using (var b = new SolidBrush(C(22, 38, 62)))
                g.FillRound(b, pill, 8);
            using (var p = new Pen(C(31, 62, 95)))
                g.DrawRound(p, pill, 8);
            DrawText(g, "Thời gian thực", pill.X + 8, pill.Y + 6, 82, 18, 10.5f, FontStyle.Regular, Color.White);

            Legend(g, r.X + 45, r.Y + 60, "Tải xuống (Mbps)", C(28, 164, 255));
            Legend(g, r.X + 212, r.Y + 60, "Tải lên (Mbps)", C(38, 209, 84));
            DrawText(g, $"Tải xuống {currentDownload:0.##}", r.X + 45, r.Y + 78, 160, 20, 10f, FontStyle.Regular, C(28, 164, 255));
            DrawText(g, $"Tải lên {currentUpload:0.##}", r.X + 212, r.Y + 78, 145, 20, 10f, FontStyle.Regular, C(38, 209, 84));
            Plot(g, new Rectangle(r.X + 55, r.Y + 106, r.Width - 95, r.Height - 152), download, upload);
        }

        private void DrawPerformance(Graphics g, Rectangle r)
        {
            Panel(g, r, "HI\u1ec6U SU\u1ea4T H\u1ec6 TH\u1ed0NG");
            var w = r.Width / 3;
            var stats = _monitorService?.GetSystemStats();
            var cpu = ClampPercent(stats?.CpuUsagePercent ?? 0);
            var ram = ClampPercent(stats?.RamUsagePercent ?? 0);
            var disk = ClampPercent((float)(_monitorService?.GetDiskUsage() ?? 0));
            Ring(g, new Rectangle(r.X + 28, r.Y + 63, 130, 130), cpu, C(33, 176, 244), "CPU Usage", "Thời gian thực", "PerformanceCounter");
            Ring(g, new Rectangle(r.X + w + 36, r.Y + 63, 130, 130), ram, C(34, 206, 85), "RAM Usage", "Thời gian thực", "Physical memory");
            Ring(g, new Rectangle(r.X + w * 2 + 42, r.Y + 63, 130, 130), disk, C(135, 55, 244), "Disk Usage", "Ổ đĩa hệ thống", "Dự phòng: trung bình");
        }

        private void DrawTable(Graphics g, Rectangle r, DashboardDevice[] rows)
        {
            Panel(g, r, $"THI\u1ebeT B\u1eca TRONG M\u1ea0NG ({rows.Length})");
            var search = new Rectangle(r.Right - 400, r.Y + 17, 250, 32);
            using (var b = new SolidBrush(C(12, 29, 51)))
                g.FillRound(b, search, 8);
            using (var p = new Pen(C(32, 61, 92)))
                g.DrawRound(p, search, 8);
            DrawSearch(g, new Rectangle(search.X + 10, search.Y + 8, 16, 16), C(114, 132, 163));
            DrawText(g, "T\u00ecm IP, Hostname, MAC...", search.X + 32, search.Y + 7, 190, 17, 12f, FontStyle.Regular, C(144, 157, 184));
            DrawRefresh(g, new Rectangle(r.Right - 134, r.Y + 22, 24, 24), C(197, 210, 233));
            var scan = new Rectangle(r.Right - 95, r.Y + 17, 77, 32);
            using (var b = new LinearGradientBrush(scan, string.Equals(_hoverKey, "dashboard-scan", StringComparison.Ordinal) ? C(89, 112, 246) : C(78, 91, 217), C(59, 57, 166), LinearGradientMode.Horizontal))
                g.FillRound(b, scan, 8);
            DrawText(g, "Qu\u00e9t l\u1ea1i", scan.X, scan.Y + 7, scan.Width, 18, 12f, FontStyle.Bold, Color.White, true);
            Hit(scan, () => _services.GetRequiredService<ScannerForm>().Show(), "dashboard-scan");

            var col = new[] { 52, 175, 305, 460, 580, 660 };
            var head = new[] { "IP Address", "Hostname", "MAC Address", "Tr\u1ea1ng th\u00e1i", "Ping", "Last Seen" };
            var y = r.Y + 62;
            using var line = new Pen(C(28, 55, 84));
            g.DrawLine(line, r.X + 20, y, r.Right - 20, y);
            for (var i = 0; i < head.Length; i++)
                DrawText(g, head[i], r.X + col[i], y + 13, 120, 18, 10.5f, FontStyle.Regular, C(213, 222, 238));

            if (rows.Length == 0)
            {
                DrawText(g, "Chưa có kết quả quét. Bấm Quét lại để mở IP Scanner.", r.X + 28, y + 62, r.Width - 56, 26, 12f, FontStyle.Regular, C(183, 196, 217));
                var viewDevicesEmpty = new Rectangle(r.X + 20, r.Bottom - 36, 205, 28);
                DrawText(g, "Xem tất cả thiết bị ->", viewDevicesEmpty.X, viewDevicesEmpty.Y + 4, viewDevicesEmpty.Width, 20, 12f, string.Equals(_hoverKey, "dashboard-devices", StringComparison.Ordinal) ? FontStyle.Bold : FontStyle.Regular, C(85, 132, 255));
                Hit(viewDevicesEmpty, () => _services.GetRequiredService<NetworkInfoForm>().Show(), "dashboard-devices");
                return;
            }

            y += 36;
            var maxVisibleRows = Math.Max(1, (r.Bottom - y - 48) / 39);
            foreach (var row in rows.Take(maxVisibleRows))
            {
                DeviceIcon(g, row.Icon, new Rectangle(r.X + 22, y + 11, 20, 20), C(230, 236, 246));
                DrawText(g, row.IpAddress, r.X + col[0], y + 9, 112, 20, 10.5f, FontStyle.Regular, Color.White);
                DrawText(g, row.Hostname, r.X + col[1], y + 9, 130, 20, 10.5f, FontStyle.Regular, Color.White);
                DrawText(g, row.MacAddress, r.X + col[2], y + 9, 150, 20, 10.5f, FontStyle.Regular, Color.White);
                using var dot = new SolidBrush(row.Online ? C(25, 213, 83) : C(244, 50, 84));
                g.FillEllipse(dot, r.X + col[3], y + 16, 10, 10);
                DrawText(g, row.Online ? "Online" : "Offline", r.X + col[3] + 20, y + 9, 90, 20, 10.5f, FontStyle.Regular, row.Online ? C(38, 231, 104) : C(255, 62, 95));
                DrawText(g, row.PingMs.HasValue ? $"{row.PingMs.Value} ms" : "-", r.X + col[4], y + 9, 65, 20, 10.5f, FontStyle.Regular, Color.White);
                DrawText(g, row.LastSeen, r.X + col[5], y + 9, 85, 20, 10.5f, FontStyle.Regular, Color.White);
                g.DrawLine(line, r.X + 20, y + 41, r.Right - 20, y + 41);
                y += 39;
            }
            if (rows.Length > maxVisibleRows)
            {
                DrawText(g, $"+{rows.Length - maxVisibleRows} thiết bị khác", r.Right - 175, r.Bottom - 36, 145, 20, 10.5f, FontStyle.Regular, C(183, 196, 217), true);
            }
            var viewDevices = new Rectangle(r.X + 20, r.Bottom - 36, 205, 28);
            DrawText(g, "Xem t\u1ea5t c\u1ea3 thi\u1ebft b\u1ecb ->", viewDevices.X, viewDevices.Y + 4, viewDevices.Width, 20, 12f, string.Equals(_hoverKey, "dashboard-devices", StringComparison.Ordinal) ? FontStyle.Bold : FontStyle.Regular, C(85, 132, 255));
            Hit(viewDevices, () => _services.GetRequiredService<NetworkInfoForm>().Show(), "dashboard-devices");
        }

        private void DrawAlerts(Graphics g, Rectangle r)
        {
            Panel(g, r, "C\u1ea2NH B\u00c1O G\u1ea6N \u0110\u00c2Y");
            var viewAlerts = new Rectangle(r.Right - 138, r.Y + 16, 116, 28);
            DrawText(g, "Xem t\u1ea5t c\u1ea3 ->", viewAlerts.X, viewAlerts.Y + 4, viewAlerts.Width, 20, 12f, string.Equals(_hoverKey, "dashboard-alerts", StringComparison.Ordinal) ? FontStyle.Bold : FontStyle.Regular, C(85, 132, 255));
            Hit(viewAlerts, () => _services.GetRequiredService<AlertsForm>().Show(), "dashboard-alerts");
            var alerts = (_dashboardState?.GetRecentAlerts(4) ?? Array.Empty<NetworkAlert>()).ToList();
            if (alerts.Count == 0)
            {
                DrawText(g, "Chưa có cảnh báo. Hãy chạy IP Scanner để cập nhật trạng thái gần nhất.", r.X + 24, r.Y + 76, r.Width - 48, 24, 11.5f, FontStyle.Regular, C(183, 196, 217));
                return;
            }

            var y = r.Y + 67;
            foreach (var alert in alerts)
            {
                var warning = alert.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase);
                Alert(g, r.X + 24, y, warning ? C(255, 52, 92) : C(15, 170, 255), warning, alert.Title, alert.Message, alert.CreatedAt.ToString("HH:mm:ss"));
                y += 66;
            }
        }

        private void Panel(Graphics g, Rectangle r, string title)
        {
            using (var b = new LinearGradientBrush(r, C(10, 29, 52), C(8, 22, 40), LinearGradientMode.ForwardDiagonal))
                g.FillRound(b, r, 13);
            using (var p = new Pen(C(28, 62, 99)))
                g.DrawRound(p, r, 13);
            DrawText(g, title, r.X + 20, r.Y + 23, 300, 24, 12.5f, FontStyle.Bold, Color.White);
        }

        private void Plot(Graphics g, Rectangle plot, IReadOnlyList<float> download, IReadOnlyList<float> upload)
        {
            using var grid = new Pen(C(27, 60, 88)) { DashStyle = DashStyle.Dot };
            using var axis = new SolidBrush(C(213, 221, 235));
            using var font = new Font("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
            var maxValue = Math.Max(1f, Math.Max(download.DefaultIfEmpty(0).Max(), upload.DefaultIfEmpty(0).Max()));
            maxValue = (float)Math.Ceiling(maxValue * 1.2f);

            for (var i = 0; i <= 5; i++)
            {
                var y = plot.Bottom - i * plot.Height / 5;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                g.DrawString($"{maxValue * i / 5:0.#}", font, axis, plot.Left - 45, y - 8);
            }
            for (var i = 0; i <= 12; i++)
                g.DrawLine(grid, plot.Left + i * plot.Width / 12, plot.Top, plot.Left + i * plot.Width / 12, plot.Bottom);

            if (download.Count < 2 && upload.Count < 2)
            {
                DrawText(g, "Đang lấy mẫu lưu lượng...", plot.Left + 20, plot.Top + plot.Height / 2 - 12, plot.Width - 40, 24, 11f, FontStyle.Regular, C(183, 196, 217), true);
            }
            else
            {
                AreaLine(g, plot, download, maxValue, C(28, 164, 255));
                AreaLine(g, plot, upload, maxValue, C(38, 209, 84));
            }

            g.DrawString("-60s", font, axis, plot.Left - 10, plot.Bottom + 13);
            g.DrawString("Bây giờ", font, axis, plot.Right - 50, plot.Bottom + 13);
        }

        private void AreaLine(Graphics g, Rectangle plot, IReadOnlyList<float> values, float maxValue, Color color)
        {
            if (values.Count == 0)
                return;

            var chartValues = values.Count == 1 ? new[] { values[0], values[0] } : values.ToArray();
            var pts = chartValues.Select((v, i) =>
            {
                var safeValue = Math.Max(0, Math.Min(v, maxValue));
                return new PointF(plot.Left + i * plot.Width / (float)(chartValues.Length - 1), plot.Bottom - safeValue / maxValue * plot.Height);
            }).ToArray();
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1].X, plot.Bottom, pts[0].X, plot.Bottom);
            area.CloseFigure();
            using var fill = new LinearGradientBrush(plot, Color.FromArgb(42, color), Color.Transparent, LinearGradientMode.Vertical);
            g.FillPath(fill, area);
            using var pen = new Pen(color, 2.2f);
            g.DrawLines(pen, pts);
            using var dot = new SolidBrush(color);
            foreach (var p in pts)
                g.FillEllipse(dot, p.X - 4, p.Y - 4, 8, 8);
        }

        private void UpdateNetworkTrafficSample()
        {
            var now = DateTime.UtcNow;
            if (_lastTrafficSampleUtc != DateTime.MinValue && (now - _lastTrafficSampleUtc).TotalMilliseconds < 900)
                return;

            try
            {
                long bytesReceived = 0;
                long bytesSent = 0;
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var stats = networkInterface.GetIPv4Statistics();
                    bytesReceived += stats.BytesReceived;
                    bytesSent += stats.BytesSent;
                }

                if (_lastBytesReceived.HasValue && _lastBytesSent.HasValue)
                {
                    var seconds = Math.Max(0.001, (now - _lastTrafficSampleUtc).TotalSeconds);
                    var downloadMbps = (float)Math.Max(0, (bytesReceived - _lastBytesReceived.Value) * 8d / seconds / 1_000_000d);
                    var uploadMbps = (float)Math.Max(0, (bytesSent - _lastBytesSent.Value) * 8d / seconds / 1_000_000d);
                    EnqueueTrafficSample(downloadMbps, uploadMbps);
                }
                else
                {
                    EnqueueTrafficSample(0, 0);
                }

                _lastBytesReceived = bytesReceived;
                _lastBytesSent = bytesSent;
                _lastTrafficSampleUtc = now;
            }
            catch
            {
                EnqueueTrafficSample(0, 0);
                _lastTrafficSampleUtc = now;
            }
        }

        private void EnqueueTrafficSample(float downloadMbps, float uploadMbps)
        {
            _downloadMbpsHistory.Enqueue(downloadMbps);
            _uploadMbpsHistory.Enqueue(uploadMbps);

            while (_downloadMbpsHistory.Count > 60)
                _downloadMbpsHistory.Dequeue();
            while (_uploadMbpsHistory.Count > 60)
                _uploadMbpsHistory.Dequeue();
        }

        private void Ring(Graphics g, Rectangle r, int value, Color color, string title, string sub1, string sub2)
        {
            using var track = new Pen(C(35, 52, 80), 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var progress = new Pen(color, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(track, r, -90, 360);
            g.DrawArc(progress, r, -90, (int)(value * 3.6f));
            DrawText(g, value + "%", r.X, r.Y + 43, r.Width, 42, 25f, FontStyle.Bold, Color.White, true);
            DrawText(g, title, r.X - 15, r.Bottom + 18, r.Width + 30, 22, 11f, FontStyle.Bold, Color.White, true);
            DrawText(g, sub1, r.X - 15, r.Bottom + 46, r.Width + 30, 20, 10.5f, FontStyle.Regular, C(192, 203, 224), true);
            DrawText(g, sub2, r.X - 15, r.Bottom + 70, r.Width + 30, 20, 10.5f, FontStyle.Regular, C(192, 203, 224), true);
        }

        private void Alert(Graphics g, int x, int y, Color color, bool warn, string title, string sub, string time)
        {
            using var pen = new Pen(color, 2.6f) { LineJoin = LineJoin.Round };
            if (warn)
            {
                g.DrawPolygon(pen, new[] { new Point(x + 18, y + 2), new Point(x + 35, y + 34), new Point(x + 1, y + 34) });
                DrawText(g, "!", x + 13, y + 12, 12, 18, 14f, FontStyle.Bold, color, true);
            }
            else
            {
                g.DrawEllipse(pen, x + 2, y + 4, 31, 31);
                DrawText(g, "i", x + 2, y + 8, 31, 20, 15f, FontStyle.Bold, color, true);
            }
            DrawText(g, title, x + 57, y + 1, 315, 23, 10.5f, FontStyle.Regular, Color.White);
            DrawText(g, sub, x + 57, y + 27, 180, 20, 10.5f, FontStyle.Regular, C(183, 196, 217));
            DrawText(g, time, x + 390, y + 2, 70, 20, 10f, FontStyle.Regular, C(151, 166, 193));
            using var line = new Pen(C(26, 54, 82));
            g.DrawLine(line, x + 57, y + 52, x + 460, y + 52);
        }

        private void Legend(Graphics g, int x, int y, string text, Color color)
        {
            using var pen = new Pen(color, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, x, y + 8, x + 20, y + 8);
            DrawText(g, text, x + 30, y - 1, 140, 20, 9.5f, FontStyle.Regular, Color.White);
        }

        private DashboardDevice[] GetDashboardDevices()
        {
            if (_dashboardState?.HasScan == true)
            {
                return _dashboardState.GetLatestDevices()
                    .Select(device => new DashboardDevice(
                        GuessDeviceIcon(device),
                        device.IpAddress,
                        "-",
                        string.IsNullOrWhiteSpace(device.MacAddress) ? "-" : device.MacAddress,
                        device.IsOnline,
                        device.ResponseTimeMs,
                        device.LastSeen?.ToString("HH:mm:ss") ?? "-"))
                    .ToArray();
            }

            return Array.Empty<DashboardDevice>();
        }

        private static string GuessDeviceIcon(NetworkDevice device)
        {
            if (device.IpAddress.EndsWith(".1", StringComparison.Ordinal))
                return "router";

            return "pc";
        }

        private static string PercentText(int value, int total)
        {
            if (total <= 0)
                return "0%";

            return $"{value * 100.0 / total:0.#}%";
        }

        private static int ClampPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;

            return Math.Max(0, Math.Min(100, (int)Math.Round(value)));
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays} ngày {uptime.Hours} giờ";

            return $"{uptime.Hours} giờ {uptime.Minutes} phút";
        }

        private void DrawText(Graphics g, string text, int x, int y, int w, int h, float size, FontStyle style, Color color, bool center = false)
        {
            const float textScale = 1.25f;
            using var brush = new SolidBrush(color);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            if (center)
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
            }
            using var font = FitFont(g, text, size * textScale, style, Math.Max(1, w), Math.Max(1, h), format);
            g.DrawString(text, font, brush, new RectangleF(x, y, Math.Max(1, w), Math.Max(1, h)), format);
        }

        private static Font FitFont(Graphics g, string text, float preferredPixels, FontStyle style, int width, int height, StringFormat format)
        {
            var current = preferredPixels;
            while (current > 8f)
            {
                var font = new Font("Segoe UI", current, style, GraphicsUnit.Pixel);
                var measured = g.MeasureString(text, font, width, format);
                if (measured.Width <= width + 1 && measured.Height <= height + 2)
                    return font;

                font.Dispose();
                current -= 1f;
            }

            return new Font("Segoe UI", 8f, style, GraphicsUnit.Pixel);
        }

        private void Hit(Rectangle r, Action? action, string key) => _hits.Add(new HitArea(r, action, key));
        private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

        private readonly record struct Stat(string Title, string Value, string Subtitle, Color Accent, string Icon);
        private readonly record struct HitArea(Rectangle Bounds, Action? Action, string Key);
        private readonly record struct DashboardDevice(string Icon, string IpAddress, string Hostname, string MacAddress, bool Online, long? PingMs, string LastSeen);

        private static void DrawLogo(Graphics g, Rectangle r)
        {
            using var p1 = new Pen(C(91, 144, 255), 4f);
            using var p2 = new Pen(C(32, 202, 233), 3f);
            g.DrawEllipse(p1, r.X + 16, r.Y + 2, 52, 60);
            g.DrawArc(p1, r.X + 3, r.Y + 17, 88, 28, 160, 310);
            g.DrawArc(p1, r.X + 3, r.Y + 17, 88, 28, -20, 310);
            g.DrawLine(p2, r.X + 45, r.Y + 5, r.X + 45, r.Y + 63);
            g.DrawLine(p2, r.X + 20, r.Y + 32, r.X + 72, r.Y + 32);
        }

        private static void DrawAtom(Graphics g, Rectangle r, Color color)
        {
            using var p = new Pen(color, 1.8f);
            g.DrawEllipse(p, r.X + 5, r.Y + 1, 12, 16);
            g.DrawArc(p, r.X, r.Y + 4, 22, 10, 160, 300);
            g.DrawArc(p, r.X, r.Y + 4, 22, 10, -20, 300);
            using var b = new SolidBrush(color);
            g.FillEllipse(b, r.X + 9, r.Y + 7, 4, 4);
        }

        private void DrawNavIcon(Graphics g, string icon, Rectangle r, Color color)
        {
            using var p = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var b = new SolidBrush(color);
            switch (icon)
            {
                case "home":
                    g.DrawLine(p, r.X + 2, r.Y + 10, r.X + 10, r.Y + 2); g.DrawLine(p, r.X + 10, r.Y + 2, r.X + 19, r.Y + 10); g.DrawRectangle(p, r.X + 5, r.Y + 10, 12, 10); break;
                case "search":
                    g.DrawEllipse(p, r.X + 1, r.Y + 1, 14, 14); g.DrawLine(p, r.X + 13, r.Y + 14, r.X + 21, r.Y + 22); break;
                case "bars":
                    g.FillRectangle(b, r.X + 2, r.Y + 10, 4, 10); g.FillRectangle(b, r.X + 9, r.Y + 3, 4, 17); g.FillRectangle(b, r.X + 16, r.Y + 7, 4, 13); break;
                case "globe":
                    g.DrawEllipse(p, r.X + 1, r.Y + 1, 18, 18); g.DrawLine(p, r.X + 2, r.Y + 10, r.X + 19, r.Y + 10); g.DrawArc(p, r.X + 5, r.Y + 1, 10, 18, 80, 200); g.DrawArc(p, r.X + 5, r.Y + 1, 10, 18, -100, 200); break;
                case "bell":
                    g.DrawArc(p, r.X + 4, r.Y + 4, 12, 12, 190, 160); g.DrawLine(p, r.X + 5, r.Y + 11, r.X + 3, r.Y + 18); g.DrawLine(p, r.X + 15, r.Y + 11, r.X + 17, r.Y + 18); g.DrawLine(p, r.X + 3, r.Y + 18, r.X + 17, r.Y + 18); break;
                case "doc":
                    g.DrawRectangle(p, r.X + 4, r.Y + 2, 13, 18); g.DrawLine(p, r.X + 7, r.Y + 8, r.X + 14, r.Y + 8); g.DrawLine(p, r.X + 7, r.Y + 13, r.X + 15, r.Y + 13); break;
                case "gear":
                    g.DrawEllipse(p, r.X + 5, r.Y + 5, 11, 11); g.DrawEllipse(p, r.X + 9, r.Y + 9, 3, 3); break;
                case "info":
                    g.DrawEllipse(p, r.X + 2, r.Y + 2, 17, 17); DrawText(g, "i", r.X + 2, r.Y + 2, 17, 17, 11f, FontStyle.Bold, color, true); break;
                default:
                    g.DrawLine(p, r.X + 10, r.Y + 2, r.X + 10, r.Y + 19); g.DrawLine(p, r.X + 2, r.Y + 10, r.X + 19, r.Y + 10); break;
            }
        }

        private static void DrawStatIcon(Graphics g, string icon, Rectangle r, Color color)
        {
            using var p = new Pen(color, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (icon == "monitor")
            {
                g.DrawRectangle(p, r.X + 18, r.Y + 18, 30, 23); g.DrawLine(p, r.X + 33, r.Y + 42, r.X + 33, r.Y + 50); g.DrawLine(p, r.X + 24, r.Y + 51, r.X + 42, r.Y + 51);
            }
            else if (icon is "shield" or "shieldx")
            {
                g.DrawPolygon(p, new[] { new Point(r.X + 33, r.Y + 13), new Point(r.X + 47, r.Y + 19), new Point(r.X + 47, r.Y + 35), new Point(r.X + 33, r.Y + 51), new Point(r.X + 19, r.Y + 35), new Point(r.X + 19, r.Y + 19) });
                if (icon == "shield") { g.DrawLine(p, r.X + 26, r.Y + 32, r.X + 32, r.Y + 38); g.DrawLine(p, r.X + 32, r.Y + 38, r.X + 42, r.Y + 26); }
                else { g.DrawLine(p, r.X + 28, r.Y + 28, r.X + 39, r.Y + 39); g.DrawLine(p, r.X + 39, r.Y + 28, r.X + 28, r.Y + 39); }
            }
            else if (icon == "pulse")
            {
                g.DrawLine(p, r.X + 14, r.Y + 35, r.X + 25, r.Y + 35); g.DrawLine(p, r.X + 25, r.Y + 35, r.X + 29, r.Y + 22); g.DrawLine(p, r.X + 29, r.Y + 22, r.X + 37, r.Y + 48); g.DrawLine(p, r.X + 37, r.Y + 48, r.X + 42, r.Y + 35); g.DrawLine(p, r.X + 42, r.Y + 35, r.X + 53, r.Y + 35);
            }
            else
            {
                DrawClockIcon(g, new Rectangle(r.X + 18, r.Y + 18, 31, 31), color);
            }
        }

        private static void DrawClockIcon(Graphics g, Rectangle r, Color color)
        {
            using var p = new Pen(color, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(p, r); g.DrawLine(p, r.X + r.Width / 2, r.Y + r.Height / 2, r.X + r.Width / 2, r.Y + 9); g.DrawLine(p, r.X + r.Width / 2, r.Y + r.Height / 2, r.X + r.Width - 10, r.Y + r.Height / 2 + 5);
        }

        private static void DrawWifi(Graphics g, Rectangle r, Color color)
        {
            using var p = new Pen(color, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(p, r.X + 1, r.Y + 2, r.Width - 2, 18, 215, 110); g.DrawArc(p, r.X + 7, r.Y + 8, r.Width - 14, 12, 215, 110);
            using var b = new SolidBrush(color); g.FillEllipse(b, r.X + r.Width / 2 - 3, r.Y + 18, 6, 6);
        }

        private static void DrawSearch(Graphics g, Rectangle r, Color color)
        {
            using var p = new Pen(color, 2f); g.DrawEllipse(p, r.X, r.Y, 11, 11); g.DrawLine(p, r.X + 10, r.Y + 10, r.X + 16, r.Y + 16);
        }

        private static void DrawRefresh(Graphics g, Rectangle r, Color color)
        {
            using var p = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(p, r.X + 2, r.Y + 3, 17, 17, 40, 230); g.DrawArc(p, r.X + 5, r.Y + 3, 17, 17, 220, 230);
        }

        private static void DeviceIcon(Graphics g, string icon, Rectangle r, Color color)
        {
            using var p = new Pen(color, 1.8f);
            if (icon is "pc" or "laptop") { g.DrawRectangle(p, r.X + 2, r.Y + 2, 16, 11); g.DrawLine(p, r.X + 10, r.Y + 13, r.X + 10, r.Y + 17); g.DrawLine(p, r.X + 5, r.Y + 18, r.X + 15, r.Y + 18); }
            else if (icon == "phone") { g.DrawRound(p, new Rectangle(r.X + 5, r.Y + 1, 10, 18), 2); g.DrawEllipse(p, r.X + 9, r.Y + 15, 2, 2); }
            else if (icon == "printer") { g.DrawRectangle(p, r.X + 3, r.Y + 7, 14, 9); g.DrawRectangle(p, r.X + 5, r.Y + 2, 10, 6); g.DrawRectangle(p, r.X + 5, r.Y + 14, 10, 5); }
            else { g.DrawRectangle(p, r.X + 3, r.Y + 8, 14, 6); g.DrawEllipse(p, r.X + 5, r.Y + 3, 3, 3); g.DrawEllipse(p, r.X + 12, r.Y + 3, 3, 3); }
        }
    }

    internal static class DrawingExtensions
    {
        public static GraphicsPath RoundPath(this Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(this Graphics g, Brush brush, Rectangle r, int radius)
        {
            using var path = r.RoundPath(radius);
            g.FillPath(brush, path);
        }

        public static void DrawRound(this Graphics g, Pen pen, Rectangle r, int radius)
        {
            using var path = r.RoundPath(radius);
            g.DrawPath(pen, path);
        }
    }
}

