using System.Drawing.Drawing2D;

namespace NetworkAdmin.Server.UI;

internal sealed class TelemetryView : DashboardCard
{
    private readonly Label _status = DashboardTheme.Label("Select a managed client", 9, color: DashboardTheme.Muted);
    private readonly Label _timestamp = DashboardTheme.Label("No telemetry received", 8, color: DashboardTheme.Muted);
    private readonly UsageGauge _cpu = new("CPU Usage", DashboardTheme.Blue);
    private readonly UsageGauge _ram = new("RAM Usage", DashboardTheme.Green);
    private readonly HistoryChart _chart = new();
    public TelemetryView() : base("System Monitoring")
    {
        var layout = DashboardTheme.Rows(23, -65, 21, -35, 22);
        var gauges = DashboardTheme.Table(2, 1);
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        gauges.Controls.Add(_cpu, 0, 0); gauges.Controls.Add(_ram, 1, 0);
        layout.Controls.Add(_status, 0, 0); layout.Controls.Add(gauges, 0, 1);
        layout.Controls.Add(DashboardTheme.Label("Last 60 samples     CPU / blue     RAM / green", 8, color: DashboardTheme.Muted), 0, 2);
        layout.Controls.Add(_chart, 0, 3); layout.Controls.Add(_timestamp, 0, 4);
        Body.Controls.Add(layout);
    }
    public void UpdateClient(ClientSnapshot? client, IReadOnlyList<TelemetryPoint> points)
    {
        var stale = client?.Connected != true || client.LastTelemetryAt is null || DateTime.UtcNow - client.LastTelemetryAt > TimeSpan.FromSeconds(10);
        Heading.Text = "System Monitoring" + (client is null ? "" : stale ? " / STALE DATA" : " / Live");
        _status.Text = client is null ? "Select a managed client to start monitoring."
            : !client.Connected ? $"OFFLINE - {client.Hostname} - last received values"
            : client.LastTelemetryAt is null ? $"{client.Hostname} - waiting for telemetry"
            : stale ? $"{client.Hostname} - waiting for a fresh sample" : $"{client.Hostname}  |  {client.IpAddress}";
        _status.ForeColor = client is not null && stale ? DashboardTheme.Red : DashboardTheme.Muted;
        _timestamp.Text = "Last telemetry: " + (client?.LastTelemetryAt?.ToLocalTime().ToString("G") ?? "--");
        _cpu.SetValue(client?.Cpu, stale); _ram.SetValue(client?.Ram, stale);
        _chart.SetPoints(points, stale);
    }
}

internal sealed class UsageGauge : Control
{
    private readonly string _caption;
    private readonly Color _accent;
    private float? _value;
    private bool _stale;
    public UsageGauge(string caption, Color accent)
    {
        _caption = caption; _accent = accent; Dock = DockStyle.Fill; DoubleBuffered = true; BackColor = DashboardTheme.Surface;
        AccessibleName = caption;
    }
    public void SetValue(float? value, bool stale)
    {
        if (_value == value && _stale == stale) return;
        _value = value; _stale = stale;
        AccessibleDescription = DashboardTheme.Percent(value) + (stale ? " stale" : " live"); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var size = Math.Min(Width - (int)(22 * scale), Height - (int)(40 * scale));
        if (size < 20) return;
        var circle = new Rectangle((Width - size) / 2, (int)(6 * scale), size, size);
        using var track = new Pen(DashboardTheme.Border, 7 * scale);
        using var arc = new Pen(_stale ? DashboardTheme.Muted : _accent, 7 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(track, circle);
        if (_value is float value && value > 0) g.DrawArc(arc, circle, -90, Math.Clamp(value, 0, 100) * 3.6f);
        using var font = new Font("Segoe UI", size < 85 ? 12 : 16, FontStyle.Bold);
        TextRenderer.DrawText(g, DashboardTheme.Percent(_value), font, circle, DashboardTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _caption, Font, new Rectangle(0, size + (int)(17 * scale), Width, (int)(23 * scale)), DashboardTheme.Text, TextFormatFlags.HorizontalCenter);
    }
}

internal sealed class HistoryChart : Control
{
    private IReadOnlyList<TelemetryPoint> _points = [];
    private bool _stale;
    public HistoryChart() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = DashboardTheme.Surface; AccessibleName = "Client CPU and RAM history, last 60 telemetry samples"; }
    public void SetPoints(IReadOnlyList<TelemetryPoint> points, bool stale)
    {
        if (_stale == stale && _points.SequenceEqual(points)) return;
        _points = points; _stale = stale; Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var r = new Rectangle((int)(28 * scale), (int)(8 * scale), Width - (int)(36 * scale), Height - (int)(24 * scale)); if (r.Width <= 0 || r.Height <= 0) return;
        using var grid = new Pen(DashboardTheme.Border);
        using var font = new Font("Segoe UI", 8);
        foreach (var value in new[] { 0, 50, 100 })
        {
            var y = r.Bottom - r.Height * value / 100;
            g.DrawLine(grid, r.Left, y, r.Right, y);
            TextRenderer.DrawText(g, value.ToString(), font, new Point(0, y - (int)(7 * scale)), DashboardTheme.Muted);
        }
        if (_points.Count == 0)
        {
            TextRenderer.DrawText(g, "Waiting for client samples", font, r, DashboardTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return;
        }
        DrawSeries(g, r, p => p.Cpu, DashboardTheme.Blue);
        DrawSeries(g, r, p => p.Ram, DashboardTheme.Green);
    }
    private void DrawSeries(Graphics g, Rectangle r, Func<TelemetryPoint, float> read, Color color)
    {
        using var pen = new Pen(_stale ? Color.FromArgb(130, color) : color, 2);
        var points = _points.Select((p, i) => new PointF(r.Left + r.Width * i / 59f, r.Bottom - r.Height * Math.Clamp(read(p), 0, 100) / 100f)).ToArray();
        if (points.Length > 1) g.DrawLines(pen, points);
        else { using var brush = new SolidBrush(pen.Color); g.FillEllipse(brush, points[0].X - 2, points[0].Y - 2, 4, 4); }
    }
}
