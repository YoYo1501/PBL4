using NetworkAdmin.Server.UI;
using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Models;
using System.Runtime.InteropServices;

namespace NetworkAdmin.Server;

internal sealed class ServerDashboardForm : Form
{
    private readonly ServerRuntime _runtime;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TelemetryHistory _history = new();
    private readonly HashSet<string> _knownAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox _selector = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = DashboardTheme.Input, ForeColor = DashboardTheme.Text, Margin = new Padding(5),
        DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 25, AccessibleName = "Global selected managed client"
    };
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = DashboardTheme.Background };
    private readonly Dictionary<string, Control> _pageViews = new();
    private readonly Dictionary<string, Button> _navigation = new();
    private readonly Label _serverStatus = DashboardTheme.Label("Server starting", 10, true, DashboardTheme.Green);
    private readonly Label _endpoint = DashboardTheme.Label("", 9, color: DashboardTheme.Muted);
    private readonly Label _agentCount = DashboardTheme.Label("0", 18, true);
    private readonly Label _clock = DashboardTheme.Label("", 13, true);
    private readonly Label _date = DashboardTheme.Label("", 9, color: DashboardTheme.Muted);
    private readonly Label _sidebarStatus = DashboardTheme.Label("Starting", 10, true, DashboardTheme.Green);
    private readonly Label _uptime = DashboardTheme.Label("00:00:00", 11);
    private readonly Label _settings = DashboardTheme.Label("", 11);
    private readonly MetricCard _managed = new("Managed Clients", DashboardTheme.Blue);
    private readonly MetricCard _online = new("Online Clients", DashboardTheme.Green);
    private readonly MetricCard _hosts = new("Hosts in Last Scan", DashboardTheme.Purple);
    private readonly MetricCard _pingAverage = new("Last Ping (avg)", DashboardTheme.Red);
    private readonly ClientDetailView _dashboardClient = new();
    private readonly ClientDetailView _clientsDetail = new();
    private readonly TelemetryView _dashboardTelemetry = new();
    private readonly TelemetryView _monitoringTelemetry = new();
    private readonly ScanView _dashboardScan = new(false);
    private readonly ScanView _scanner = new(true);
    private readonly PingView _dashboardPing = new(false);
    private readonly PingView _pingPage = new(true);
    private readonly DataGridView _clients = DashboardTheme.Grid(("Status", "Status", 80), ("Hostname", "Hostname", 160),
        ("IPv4", "IPv4", 115), ("MAC", "MAC", 140), ("CPU", "CPU", 60), ("RAM", "RAM", 60), ("LastSeen", "Last Seen", 140));
    private readonly DataGridView _interfaces = DashboardTheme.Grid(("Name", "Interface", 110), ("IpAddress", "IPv4", 105),
        ("MacAddress", "MAC", 130), ("Gateway", "Gateway", 105), ("Subnet", "Subnet", 110), ("Description", "Description", 180));
    private IReadOnlyList<ClientSnapshot> _snapshots = [];
    private ScanResultMessage? _lastScan;
    private PingResult? _lastPing;
    private DateTime? _scanAt;
    private DateTime? _runningSince;
    private TimeSpan _uptimeValue;
    private string _scanOrigin = "";
    private string _scanMessage = "No scan completed. Scanned hosts are not automatically managed agents.";
    private bool _scanError;
    private string? _selectedId;
    private string? _prefillSession;
    private string _selectorKey = "";
    private string _interfaceKey = "";
    private bool _updating;
    private bool _scanning;
    private bool _pinging;
    private bool _closing;
    private bool _closed;
    private Task? _pingTask;
    private Task? _scanTask;

    public ServerDashboardForm(ServerRuntime runtime)
    {
        // Build the entire control tree before allowing WinForms to consume the design DPI.
        // Scaling an empty form first leaves subsequently added pixel-sized rows unscaled.
        SuspendLayout();
        _runtime = runtime;
        Text = "Network Administration | Server";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9);
        BackColor = DashboardTheme.Background;
        ForeColor = DashboardTheme.Text;
        DoubleBuffered = true;
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        var initialScale = DeviceDpi / 96f;
        ClientSize = new Size(Math.Min(1480, (int)(screen.Width / initialScale) - 40),
            Math.Min(900, (int)(screen.Height / initialScale) - 70));
        MinimumSize = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        var shell = DashboardTheme.Table(2, 1);
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildSidebar(), 0, 0);
        var main = DashboardTheme.Rows(85, 39, -100);
        main.Padding = new Padding(18, 6, 18, 12);
        main.Controls.Add(BuildHeader(), 0, 0);
        var selection = DashboardTheme.Table(2, 1);
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selection.Controls.Add(DashboardTheme.Label("SELECTED AGENT", 8, true, DashboardTheme.Muted), 0, 0);
        selection.Controls.Add(_selector, 1, 0);
        main.Controls.Add(selection, 0, 1); main.Controls.Add(_pages, 0, 2);
        shell.Controls.Add(main, 1, 0); Controls.Add(shell);
        AddPage("Dashboard", BuildDashboard(), 755);
        AddPage("Clients", BuildClients(), 660);
        AddPage("IP Scanner", _scanner, 520);
        AddPage("Ping Tool", _pingPage, 520);
        AddPage("Monitoring", _monitoringTelemetry, 540);
        AddPage("Settings", BuildSettings(), 450);
        AddPage("About", BuildAbout(), 400);
        Navigate("Dashboard");

        _selector.DrawItem += DrawAgent;
        _selector.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            _selectedId = (_selector.SelectedItem as ClientSnapshot)?.SessionId;
            RenderSelection();
        };
        _clients.SelectionChanged += (_, _) =>
        {
            if (_updating || _clients.CurrentRow?.Tag is not string id) return;
            _selectedId = id;
            _updating = true;
            _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => c.SessionId == id);
            _updating = false;
            RenderSelection();
        };
        _dashboardPing.TargetInput.TextChanged += (_, _) => SyncTarget(_dashboardPing, _pingPage);
        _pingPage.TargetInput.TextChanged += (_, _) => SyncTarget(_pingPage, _dashboardPing);
        _dashboardPing.PingButton.Click += (_, _) => _pingTask = PingAsync();
        _pingPage.PingButton.Click += (_, _) => _pingTask = PingAsync();
        _scanner.ScanButton.Click += (_, _) => _scanTask = ScanAsync();
        _dashboardScan.ScanButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_scanner.SubnetInput.Text)) { Navigate("IP Scanner"); _scanner.SubnetInput.Focus(); }
            else _scanTask = ScanAsync();
        };
        _refresh.Tick += (_, _) => RefreshState();
        Shown += (_, _) => { _runtime.Start(); RefreshState(); _refresh.Start(); };
        FormClosing += CloseAsync;
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var enabled = 1;
        // Native title-bar color only; networking is never implemented in the form.
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private Control BuildSidebar()
    {
        var sidebar = DashboardTheme.Rows(124, 302, -100, 172);
        sidebar.BackColor = DashboardTheme.Sidebar; sidebar.Padding = new Padding(12, 12, 12, 12);
        var branding = DashboardTheme.Rows(64, 30, 24);
        branding.Controls.Add(new BrandMark(), 0, 0);
        var brand = DashboardTheme.Label("NET ADMIN", 15, true, DashboardTheme.Blue); brand.TextAlign = ContentAlignment.MiddleCenter;
        var role = DashboardTheme.Label("Server", 10, color: DashboardTheme.Blue); role.TextAlign = ContentAlignment.MiddleCenter;
        branding.Controls.Add(brand, 0, 1); branding.Controls.Add(role, 0, 2); sidebar.Controls.Add(branding, 0, 0);
        var nav = DashboardTheme.Rows(40, 40, 40, 40, 40, 16, 40, 40);
        string[] names = ["Dashboard", "Clients", "IP Scanner", "Ping Tool", "Monitoring", "Settings", "About"];
        string[] icons = ["\u25A6", "\u25A3", "\u2315", "\u2194", "\u223F", "\u2699", "\u24D8"];
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var button = DashboardTheme.Button($"{icons[i]}   {name}", false);
            button.TextAlign = ContentAlignment.MiddleLeft; button.Padding = new Padding(8, 0, 0, 0);
            button.FlatAppearance.BorderSize = 0; button.Font = new Font("Segoe UI", 10);
            button.AccessibleName = name;
            button.Click += (_, _) => Navigate(name);
            _navigation[name] = button; nav.Controls.Add(button, 0, i < 5 ? i : i + 1);
        }
        var separator = new Panel { Dock = DockStyle.Fill, Height = 1, BackColor = DashboardTheme.Border, Margin = new Padding(10, 7, 10, 8) };
        nav.Controls.Add(separator, 0, 5); sidebar.Controls.Add(nav, 0, 1);
        var status = new DashboardCard("Server Status") { Margin = Padding.Empty, Padding = new Padding(10, 5, 10, 8) };
        var statusRows = DashboardTheme.Rows(27, 21, 24, 21, 24);
        statusRows.Controls.Add(_sidebarStatus, 0, 0);
        statusRows.Controls.Add(DashboardTheme.Label("Listening Address", 8, color: DashboardTheme.Muted), 0, 1);
        statusRows.Controls.Add(DashboardTheme.Label(_runtime.Endpoint, 10), 0, 2);
        statusRows.Controls.Add(DashboardTheme.Label("Uptime", 8, color: DashboardTheme.Muted), 0, 3);
        statusRows.Controls.Add(_uptime, 0, 4); status.Body.Controls.Add(statusRows); sidebar.Controls.Add(status, 0, 3);
        return sidebar;
    }

    private Control BuildHeader()
    {
        var header = DashboardTheme.Table(4, 1);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        var title = DashboardTheme.Rows(40, 24);
        title.Controls.Add(DashboardTheme.Label("Network Administration", 20, true), 0, 0);
        title.Controls.Add(DashboardTheme.Label("Centralized LAN Monitoring & Management", 10, color: DashboardTheme.Muted), 0, 1);
        header.Controls.Add(title, 0, 0);
        var server = DashboardTheme.Rows(32, 24); server.Padding = new Padding(10, 6, 0, 0);
        server.Controls.Add(_serverStatus, 0, 0); server.Controls.Add(_endpoint, 0, 1); header.Controls.Add(server, 1, 0);
        var count = DashboardTheme.Rows(29, 29); count.Padding = new Padding(8, 6, 0, 0);
        count.Controls.Add(DashboardTheme.Label("Connected Agents", 9, color: DashboardTheme.Muted), 0, 0);
        count.Controls.Add(_agentCount, 0, 1); header.Controls.Add(count, 2, 0);
        var time = DashboardTheme.Rows(32, 24); time.Padding = new Padding(10, 6, 0, 0);
        time.Controls.Add(_clock, 0, 0); time.Controls.Add(_date, 0, 1); header.Controls.Add(time, 3, 0);
        return header;
    }

    private Control BuildDashboard()
    {
        var dashboard = DashboardTheme.Rows(-14, -39, -47);
        var cards = DashboardTheme.Table(4, 1);
        foreach (var card in new[] { _managed, _online, _hosts, _pingAverage })
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); cards.Controls.Add(card);
        }
        dashboard.Controls.Add(cards, 0, 0);
        var middle = DashboardTheme.Table(2, 1);
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        middle.Controls.Add(_dashboardClient, 0, 0); middle.Controls.Add(_dashboardTelemetry, 1, 0);
        dashboard.Controls.Add(middle, 0, 1);
        var bottom = DashboardTheme.Table(2, 1);
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        bottom.Controls.Add(_dashboardScan, 0, 0); bottom.Controls.Add(_dashboardPing, 1, 0);
        dashboard.Controls.Add(bottom, 0, 2);
        return dashboard;
    }

    private Control BuildClients()
    {
        var page = DashboardTheme.Rows(-100, 280, 190);
        var fleet = new DashboardCard("Managed Stations / Select a row to manage that session"); fleet.Body.Controls.Add(_clients);
        page.Controls.Add(fleet, 0, 0); page.Controls.Add(_clientsDetail, 0, 1);
        var adapters = new DashboardCard("All Reported Interfaces / Physical and virtual adapters"); adapters.Body.Controls.Add(_interfaces);
        page.Controls.Add(adapters, 0, 2);
        return page;
    }
    private Control BuildSettings()
    {
        var card = new DashboardCard("Server Configuration");
        card.Body.Controls.Add(_settings); return card;
    }
    private static Control BuildAbout()
    {
        var card = new DashboardCard("About / Network Administration Server");
        card.Body.Controls.Add(DashboardTheme.Label(
            "NET ADMIN - Centralized LAN Monitoring & Management\n\n" +
            "Manage connected client agents from one administrator dashboard.\n" +
            "Network discovery, remote four-request Ping, and live client CPU/RAM.\n\n" +
            "Managed clients are connected agents. Discovered hosts may have no agent.\n" +
            "All telemetry belongs to the selected client; commands run on that agent.\n\n" +
            "Phase 10.1 - Native WinForms / in-memory monitoring", 12));
        return card;
    }
    private void AddPage(string name, Control content, int minimumHeight)
    {
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DashboardTheme.Background };
        content.Dock = DockStyle.None; content.Location = Point.Empty; content.Margin = Padding.Empty;
        viewport.Controls.Add(content);
        viewport.Resize += (_, _) =>
        {
            var min = (int)(minimumHeight * DeviceDpi / 96f);
            var height = Math.Max(min, viewport.ClientSize.Height);
            content.Size = new Size(Math.Max(100, viewport.ClientSize.Width - (height > viewport.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0)), height);
        };
        _pageViews[name] = viewport; _pages.Controls.Add(viewport);
    }
    private void Navigate(string name)
    {
        foreach (var (key, page) in _pageViews) page.Visible = key == name;
        if (_pageViews.TryGetValue(name, out var active)) active.BringToFront();
        foreach (var (key, button) in _navigation)
        {
            button.BackColor = key == name ? Color.FromArgb(8, 73, 168) : DashboardTheme.Sidebar;
            button.ForeColor = key == name ? Color.White : DashboardTheme.Muted;
        }
    }
    private static void SyncTarget(PingView source, PingView target)
    {
        if (target.TargetInput.Text != source.TargetInput.Text) target.TargetInput.Text = source.TargetInput.Text;
    }
    private void DrawAgent(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        var c = e.Index >= 0 ? _selector.Items[e.Index] as ClientSnapshot : null;
        var text = c is null ? "No agent selected - choose from Clients" : $"{c.Hostname}  |  {c.IpAddress}  |  {(c.Connected ? "Online" : "Offline")}  |  Session {c.SessionId[..Math.Min(8, c.SessionId.Length)]}";
        TextRenderer.DrawText(e.Graphics, text, _selector.Font, e.Bounds, DashboardTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
    private ClientSnapshot? Selected => _snapshots.FirstOrDefault(c => c.SessionId == _selectedId);

    private void RefreshState()
    {
        if (_closing) return;
        _snapshots = _runtime.GetClients();
        _history.Observe(_snapshots);
        foreach (var c in _snapshots) _knownAgents.Add(c.ClientId);
        if (_runtime.IsRunning)
        {
            _runningSince ??= DateTime.UtcNow;
            _uptimeValue = DateTime.UtcNow - _runningSince.Value;
        }
        _uptime.Text = $"{(int)_uptimeValue.TotalHours:00}:{_uptimeValue.Minutes:00}:{_uptimeValue.Seconds:00}";
        _serverStatus.Text = _runtime.IsRunning ? "\u25CF  Server Running" : "\u25CB  Server Stopped";
        _serverStatus.ForeColor = _runtime.IsRunning ? DashboardTheme.Green : DashboardTheme.Red;
        _sidebarStatus.Text = _runtime.IsRunning ? "\u25CF  Running" : "\u25CB  Stopped";
        _sidebarStatus.ForeColor = _serverStatus.ForeColor;
        _endpoint.Text = (_runtime.IsRunning ? "Listening on " : "Endpoint: ") + _runtime.Endpoint;
        var online = _snapshots.Where(c => c.Connected).Select(c => c.ClientId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _agentCount.Text = online.ToString();
        _clock.Text = DateTime.Now.ToString("hh:mm:ss tt"); _date.Text = DateTime.Now.ToString("ddd, MMM d, yyyy");
        _managed.UpdateValue(_knownAgents.Count.ToString(), "Known agents this server run");
        _online.UpdateValue(online.ToString(), "Currently connected agents");
        _hosts.UpdateValue(_lastScan?.Devices.Count.ToString() ?? "--", _scanAt is DateTime at ? $"Last scan: {at:t}" : "No scan completed");
        _pingAverage.UpdateValue(_lastPing?.AverageResponseTimeMs is double ms ? $"{ms:0.#} ms" : "--", _lastPing is null ? "No ping completed" : $"Target: {_lastPing.Host}");
        _settings.Text = $"Runtime status: {_runtime.Status}\nListening address: {_runtime.Endpoint}\n\n" +
            "Configuration is read at startup. Restart with --host <address> --port <port> to change it.\n" +
            "Use --host 0.0.0.0 to listen on all IPv4 interfaces.\n\n" +
            "Dashboard refresh: 500 ms\nHistory: last 60 received samples per session, in memory\n" +
            "Stale telemetry threshold: 10 seconds\nRemote scanner: IPv4 unicast /24 through /30\n\n" +
            "No connection settings are changed by this page.";
        _updating = true;
        try
        {
            if (_selectedId is null) _selectedId = _snapshots.FirstOrDefault(c => c.Connected)?.SessionId;
            var key = string.Join(";", _snapshots.Select(c => c.ToString()));
            if (key != _selectorKey)
            {
                _selectorKey = key; _selector.Items.Clear(); _selector.Items.AddRange(_snapshots.Cast<object>().ToArray());
                _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => c.SessionId == _selectedId);
            }
            var ids = _snapshots.Select(c => c.SessionId).ToHashSet();
            foreach (var row in _clients.Rows.Cast<DataGridViewRow>().Where(r => !ids.Contains((string)r.Tag!)).ToArray()) _clients.Rows.Remove(row);
            foreach (var c in _snapshots)
            {
                var row = _clients.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (string?)r.Tag == c.SessionId);
                if (row is null) { row = _clients.Rows[_clients.Rows.Add()]; row.Tag = c.SessionId; }
                row.SetValues(c.Connected ? "Online" : "Offline", c.Hostname, c.IpAddress, DashboardTheme.Value(c.MacAddress),
                    c.Connected ? DashboardTheme.Percent(c.Cpu) : "--", c.Connected ? DashboardTheme.Percent(c.Ram) : "--", c.LastSeen.ToLocalTime().ToString("G"));
                row.Cells[0].Style.ForeColor = c.Connected ? DashboardTheme.Green : DashboardTheme.Red;
                row.Selected = c.SessionId == _selectedId;
            }
        }
        finally { _updating = false; }
        RenderSelection(); RenderScan();
    }

    private void RenderSelection()
    {
        var c = Selected;
        _dashboardClient.UpdateClient(c); _clientsDetail.UpdateClient(c);
        var samples = _history.For(c?.SessionId);
        _dashboardTelemetry.UpdateClient(c, samples); _monitoringTelemetry.UpdateClient(c, samples);
        var enabled = c?.Connected == true && _runtime.IsRunning && !_closing;
        _dashboardPing.SetBusy(enabled && !_pinging, _pinging); _pingPage.SetBusy(enabled && !_pinging, _pinging);
        _scanner.SetAgent(c, enabled && !_scanning, _scanning); _dashboardScan.SetAgent(c, enabled && !_scanning, _scanning);
        var key = c?.SessionId + string.Join(";", c?.Interfaces ?? []);
        if (_interfaceKey != key)
        {
            _interfaceKey = key; _interfaces.Rows.Clear();
            foreach (var nic in c?.Interfaces ?? []) _interfaces.Rows.Add(nic.Name, nic.IpAddress, nic.MacAddress, nic.Gateway, nic.Subnet, nic.Description);
            _interfaces.ClearSelection();
        }
        if (c?.PrimaryInterface is { } primary && _prefillSession != c.SessionId)
        {
            _prefillSession = c.SessionId; _scanner.SubnetInput.Text = primary.Subnet;
        }
    }
    private void RenderScan()
    {
        var rows = _lastScan is null ? Array.Empty<ScanHostSnapshot>() : ServerRuntime.MatchScan(_lastScan, _snapshots);
        var summary = _scanMessage;
        if (_lastScan is not null && !_scanning && !_scanError)
            summary = $"{rows.Count} hosts found  |  {rows.Count(r => r.ManagedClient == "Yes")} managed hosts  |  Completed {_scanAt:t}\n{_scanOrigin}";
        _dashboardScan.SetResults(rows, summary, _scanError); _scanner.SetResults(rows, summary, _scanError);
    }
    private void PingStatus(string status, bool error = false)
    {
        _dashboardPing.SetStatus(status, error); _pingPage.SetStatus(status, error);
    }
    private async Task PingAsync()
    {
        var c = Selected;
        if (c?.Connected != true || _pinging) return;
        _pinging = true; RenderSelection();
        var target = _pingPage.TargetInput.Text.Trim();
        var origin = $"Agent: {c.Hostname} ({c.IpAddress}) / Session {c.SessionId[..Math.Min(8, c.SessionId.Length)]}";
        PingStatus($"Pinging {target}... Previous completed results remain visible.\n{origin}");
        try
        {
            var result = await _runtime.PingAsync(c.SessionId, target, _lifetime.Token);
            if (_closing) return;
            _lastPing = result; _dashboardPing.SetResult(result); _pingPage.SetResult(result);
            PingStatus($"{result.StatusMessage} | Target: {result.Host} | {DateTime.Now:T}\n{origin}", !result.Success);
        }
        catch (Exception ex) { if (!_closing) PingStatus($"Ping failed: {ex.Message} (previous results retained)\n{origin}", true); }
        finally { _pinging = false; if (!_closing) RefreshState(); }
    }
    private async Task ScanAsync()
    {
        var c = Selected;
        if (c?.Connected != true || _scanning) return;
        _scanning = true; _scanError = false; RenderSelection();
        var subnet = _scanner.SubnetInput.Text;
        var origin = $"Agent: {c.Hostname} ({c.IpAddress}) | Subnet: {subnet}";
        _scanMessage = $"Scanning... Previous completed results remain visible.\n{origin}"; RenderScan();
        try
        {
            var result = await _runtime.ScanAsync(c.SessionId, subnet, _lifetime.Token);
            if (_closing) return;
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
            _lastScan = result; _scanAt = DateTime.Now; _scanOrigin = origin;
        }
        catch (Exception ex)
        {
            if (!_closing) { _scanError = true; _scanMessage = $"Scan failed: {ex.Message}\nPrevious completed results retained."; }
        }
        finally { _scanning = false; if (!_closing) RefreshState(); }
    }
    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _refresh.Stop(); Enabled = false;
        try
        {
            await _lifetime.CancelAsync(); await _runtime.StopAsync();
            await Task.WhenAll(_pingTask ?? Task.CompletedTask, _scanTask ?? Task.CompletedTask);
        }
        catch (Exception ex) { Console.WriteLine($"Server shutdown: {ex.Message}"); }
        finally { _closed = true; Close(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
