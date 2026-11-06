using NetworkAdmin.Server.UI;
using System.Runtime.InteropServices;

using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.UI.Forms;

internal sealed class ServerDashboardForm : Form
{
    private readonly IServerRuntime _runtime;
    private readonly ServerDashboardPresenter _presenter;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _knownAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openingScreenClients = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly TextBox _settings = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = DashboardTheme.Surface, ForeColor = DashboardTheme.Text, Font = new Font("Segoe UI", 11), ScrollBars = ScrollBars.Vertical };
    private readonly MetricCard _managed = new("Managed Clients", DashboardTheme.Blue);
    private readonly MetricCard _online = new("Online Clients", DashboardTheme.Green);
    private readonly MetricCard _hosts = new("Hosts in Last Scan", DashboardTheme.Purple);
    private readonly MetricCard _pingAverage = new("Last Ping (avg)", DashboardTheme.Red);
    private readonly ClientDetailView _dashboardClient = new();
    private readonly ClientDetailView _clientsDetail = new();
    private readonly TelemetryView _dashboardTelemetry = new();
    private readonly TelemetryView _monitoringTelemetry = new();
    private readonly Label _monitoringStatus = DashboardTheme.Label("Alert stream idle; waiting for Client monitoring.", 10, color: DashboardTheme.Muted);
    private readonly Label _autoDisconnectStatus = DashboardTheme.Label("Disabled: alerts only; sessions are not stopped automatically.", 9, color: DashboardTheme.Muted);
    private readonly Button _autoDisconnectButton = DashboardTheme.Button("Enable auto-disconnect", false);
    private readonly ListBox _securityAlerts = new()
    {
        Dock = DockStyle.Fill,
        BackColor = DashboardTheme.Surface,
        ForeColor = DashboardTheme.Text,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        HorizontalScrollbar = true,
        AccessibleName = "Security alerts"
    };
    private readonly object _alertSync = new();
    private readonly List<SecurityAlertMessage> _securityAlertHistory = [];
    private readonly ScanView _dashboardScan = new(false);
    private readonly ScanView _scanner = new(true);
    private readonly PingView _dashboardPing = new(false);
    private readonly PingView _pingPage = new(true);
    private readonly Button _agentInfoButton = DashboardTheme.Button("Get Agent Info");
    private readonly Button _chatButton = DashboardTheme.Button("Open Chat");
    private readonly Button _viewScreenButton = DashboardTheme.Button("View Screen");
    private readonly Label _agentInfoStatus = DashboardTheme.Label("No remote command executed for this session.", 9, color: DashboardTheme.Muted);
    private readonly Button _shutdownButton = DashboardTheme.Button("Shutdown", false);
    private readonly Button _restartButton = DashboardTheme.Button("Restart", false);
    private readonly Button _cancelPowerButton = DashboardTheme.Button("Cancel", false);
    private readonly NumericUpDown _powerCountdown = new()
    {
        Minimum = RemoteAdministrationCommandPolicy.MinimumCountdownSeconds,
        Maximum = RemoteAdministrationCommandPolicy.MaximumCountdownSeconds,
        Value = RemoteAdministrationCommandPolicy.DefaultCountdownSeconds,
        Dock = DockStyle.Fill,
        BackColor = DashboardTheme.Input,
        ForeColor = DashboardTheme.Text,
        Margin = new Padding(4),
        TextAlign = HorizontalAlignment.Center
    };
    private readonly Label _powerStatus = DashboardTheme.Label("No shutdown or restart command executed for this session.", 9, color: DashboardTheme.Muted);
    private readonly Button _lockButton = DashboardTheme.Button("Lock", false);
    private readonly Button _unlockButton = DashboardTheme.Button("Unlock", false);
    private readonly ComboBox _lockMode = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = DashboardTheme.Input,
        ForeColor = DashboardTheme.Text,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4)
    };
    private readonly Label _lockStatus = DashboardTheme.Label("No lock command executed for this session.", 9, color: DashboardTheme.Muted);
    private readonly DataGridView _clients = DashboardTheme.Grid(("Status", "Status", 80), ("Hostname", "Hostname", 160),
        ("IPv4", "IPv4", 115), ("MAC", "MAC", 140), ("CPU", "CPU", 60), ("RAM", "RAM", 60), ("LastSeen", "Last Seen", 140));
    private readonly DataGridView _interfaces = DashboardTheme.Grid(("Name", "Interface", 110), ("IpAddress", "IPv4", 105),
        ("MacAddress", "MAC", 130), ("Gateway", "Gateway", 105), ("Subnet", "Subnet", 110), ("Description", "Description", 180));
    private IReadOnlyList<ClientSnapshot> _snapshots = [];
    // Retained as a view of presenter state for existing layout validation and diagnostics.
    private readonly Dictionary<string, SessionCommandState> _commands;
    private DateTime? _runningSince;
    private TimeSpan _uptimeValue;
    private string? _selectedId;
    private string? _prefillKey;
    private string _selectorKey = "";
    private string _interfaceKey = "";
    private bool _updating;
    private bool _closing;
    private bool _closed;
    private Task? _pingTask;
    private Task? _scanTask;
    private Task? _agentInfoTask;
    private Task? _powerTask;
    private CancellationTokenSource? _powerCancellation;
    private Task? _lockTask;
    private CancellationTokenSource? _lockCancellation;

    public ServerDashboardForm(IServerRuntime runtime)
    {
        // Build the entire control tree before allowing WinForms to consume the design DPI.
        // Scaling an empty form first leaves subsequently added pixel-sized rows unscaled.
        SuspendLayout();
        _runtime = runtime;
        _presenter = new ServerDashboardPresenter(runtime);
        _commands = _presenter.Commands;
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
        AddPage("Monitoring", BuildMonitoring(), 620);
        AddPage("Settings", BuildSettings(), 450);
        AddPage("About", BuildAbout(), 400);
        Navigate("Dashboard");

        _selector.DrawItem += DrawAgent;
        _selector.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            _selectedId = (_selector.SelectedItem as ClientSnapshot)?.ClientId;
            RenderSelection();
        };
        _clients.SelectionChanged += (_, _) =>
        {
            if (_updating || _clients.CurrentRow?.Tag is not string id) return;
            _selectedId = id;
            _updating = true;
            _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => string.Equals(c.ClientId, id, StringComparison.OrdinalIgnoreCase));
            _updating = false;
            RenderSelection();
        };
        _dashboardPing.TargetInput.TextChanged += (_, _) => SyncTarget(_dashboardPing, _pingPage);
        _pingPage.TargetInput.TextChanged += (_, _) => SyncTarget(_pingPage, _dashboardPing);
        _dashboardPing.PingButton.Click += (_, _) => _pingTask = PingAsync();
        _pingPage.PingButton.Click += (_, _) => _pingTask = PingAsync();
        _scanner.ScanButton.Click += (_, _) => _scanTask = ScanAsync();
        _agentInfoButton.Click += (_, _) => _agentInfoTask = GetAgentInfoAsync();
        _chatButton.Click += (_, _) => _ = OpenChatAsync();
        _viewScreenButton.Click += (_, _) => OpenScreenViewer();
        _autoDisconnectButton.Click += (_, _) => ToggleAutoDisconnect();
        _shutdownButton.Click += (_, _) => _powerTask = PowerAsync(RemoteAdministrationCommandType.Shutdown);
        _restartButton.Click += (_, _) => _powerTask = PowerAsync(RemoteAdministrationCommandType.Restart);
        _cancelPowerButton.Click += (_, _) => _powerCancellation?.Cancel();
        _lockMode.Items.AddRange(["Windows Lock (unlock requires Windows sign-in)", "Managed Workstation Lock (policy required)"]);
        _lockMode.SelectedIndex = 0;
        _lockButton.Click += (_, _) => _lockTask = LockAsync(true);
        _unlockButton.Click += (_, _) => _lockTask = LockAsync(false);
        _dashboardScan.ScanButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_scanner.SubnetInput.Text)) { Navigate("IP Scanner"); _scanner.SubnetInput.Focus(); }
            else _scanTask = ScanAsync();
        };
        _refresh.Tick += (_, _) => RefreshState();
        _runtime.SecurityAlertReceived += OnSecurityAlertReceived;
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
        var page = DashboardTheme.Rows(-100, 280, 190, 210);
        var fleet = new DashboardCard("Managed Stations / Select a row to manage that session"); fleet.Body.Controls.Add(_clients);
        page.Controls.Add(fleet, 0, 0); page.Controls.Add(_clientsDetail, 0, 1);
        var adapters = new DashboardCard("All Reported Interfaces / Physical and virtual adapters"); adapters.Body.Controls.Add(_interfaces);
        page.Controls.Add(adapters, 0, 2);
        var remote = new DashboardCard("Remote Administration");
        var remoteRows = DashboardTheme.Rows(38, 30, 30, 34, 30);
        var remoteActions = DashboardTheme.Table(5, 1);
        foreach (var width in new[] { 155, 125, 125, 105, 120 })
            remoteActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        remoteActions.Controls.Add(_agentInfoButton, 0, 0);
        remoteActions.Controls.Add(_shutdownButton, 1, 0);
        remoteActions.Controls.Add(_restartButton, 2, 0);
        remoteActions.Controls.Add(_cancelPowerButton, 3, 0);
        var countdown = DashboardTheme.Table(2, 1);
        countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        countdown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        countdown.Controls.Add(DashboardTheme.Label("Delay (s)", 8, color: DashboardTheme.Muted), 0, 0);
        countdown.Controls.Add(_powerCountdown, 1, 0);
        remoteActions.Controls.Add(countdown, 4, 0);
        remoteRows.Controls.Add(remoteActions, 0, 0);
        remoteRows.Controls.Add(_agentInfoStatus, 0, 1);
        remoteRows.Controls.Add(_powerStatus, 0, 2);
        var lockActions = DashboardTheme.Table(5, 1);
        lockActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        lockActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        lockActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lockActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        lockActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        lockActions.Controls.Add(_lockButton, 0, 0);
        lockActions.Controls.Add(_unlockButton, 1, 0);
        lockActions.Controls.Add(_lockMode, 2, 0);
        lockActions.Controls.Add(_chatButton, 3, 0);
        lockActions.Controls.Add(_viewScreenButton, 4, 0);
        remoteRows.Controls.Add(lockActions, 0, 3);
        remoteRows.Controls.Add(_lockStatus, 0, 4);
        remote.Body.Controls.Add(remoteRows);
        page.Controls.Add(remote, 0, 3);
        return page;
    }
    private Control BuildSettings()
    {
        var card = new DashboardCard("Server Configuration");
        card.Body.Controls.Add(_settings); return card;
    }

    private Control BuildMonitoring()
    {
        var page = DashboardTheme.Rows(230, -100);
        var policy = new DashboardCard("AI Security Monitoring / Runtime policy");
        var policyRows = DashboardTheme.Rows(34, 34, 42, -100);
        policyRows.Controls.Add(_monitoringStatus, 0, 0);
        policyRows.Controls.Add(_autoDisconnectStatus, 0, 1);
        policyRows.Controls.Add(_autoDisconnectButton, 0, 2);
        policyRows.Controls.Add(_monitoringTelemetry, 0, 3);
        policy.Body.Controls.Add(policyRows);
        page.Controls.Add(policy, 0, 0);

        var alerts = new DashboardCard("Security Alerts / newest first");
        alerts.Body.Controls.Add(_securityAlerts);
        page.Controls.Add(alerts, 0, 1);
        return page;
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
    private ClientSnapshot? Selected => _snapshots.FirstOrDefault(c => string.Equals(c.ClientId, _selectedId, StringComparison.OrdinalIgnoreCase));

    private void RefreshState()
    {
        if (_closing) return;
        _snapshots = _runtime.GetClients();
        _presenter.ObserveClients(_snapshots);
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
        _settings.Text = $"Runtime status: {_runtime.Status}\nListening address: {_runtime.Endpoint}\n\n" +
            "Configuration is read at startup. Restart with --host <address> --port <port> to change it.\n" +
            "Use --host 0.0.0.0 to listen on all IPv4 interfaces.\n\n" +
            "Control and screen TCP channels require TLS 1.2 or newer; Clients must pin the Server SHA-256 fingerprint shown in Settings/Endpoint. The Server certificate is stored in the current Windows user certificate store.\n\n" +
            "Dashboard refresh: 500 ms\nHistory: last 60 received samples per session, in memory\n" +
            "Stale telemetry threshold: 10 seconds\nRemote scanner: IPv4 unicast /23 through /30 (510 hosts maximum)\n\n" +
            "No connection settings are changed by this page.";
        RenderSecurityMonitoring();
        _updating = true;
        try
        {
            if (_selectedId is null) _selectedId = _snapshots.FirstOrDefault(c => c.Connected)?.ClientId;
            var key = string.Join(";", _snapshots.Select(c => c.ToString()));
            if (key != _selectorKey)
            {
                _selectorKey = key; _selector.Items.Clear(); _selector.Items.AddRange(_snapshots.Cast<object>().ToArray());
                _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => string.Equals(c.ClientId, _selectedId, StringComparison.OrdinalIgnoreCase));
            }
            var ids = _snapshots.Select(c => c.ClientId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _clients.Rows.Cast<DataGridViewRow>().Where(r => !ids.Contains((string)r.Tag!)).ToArray()) _clients.Rows.Remove(row);
            foreach (var c in _snapshots)
            {
                var row = _clients.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => string.Equals((string?)r.Tag, c.ClientId, StringComparison.OrdinalIgnoreCase));
                if (row is null) { row = _clients.Rows[_clients.Rows.Add()]; row.Tag = c.ClientId; }
                row.SetValues(c.Connected ? "Online" : "Offline", c.Hostname, c.IpAddress, DashboardTheme.Value(c.MacAddress),
                    c.HasFreshTelemetry(DateTime.UtcNow) ? DashboardTheme.Percent(c.Cpu) : "--",
                    c.HasFreshTelemetry(DateTime.UtcNow) ? DashboardTheme.Percent(c.Ram) : "--", c.LastSeen.ToLocalTime().ToString("G"));
                row.Cells[0].Style.ForeColor = c.Connected ? DashboardTheme.Green : DashboardTheme.Red;
                row.Selected = string.Equals(c.ClientId, _selectedId, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { _updating = false; }
        RenderSelection();
    }

    private void RenderSelection()
    {
        var c = Selected;
        var state = _presenter.CommandsFor(c?.ClientId);
        _dashboardClient.UpdateClient(c); _clientsDetail.UpdateClient(c);
        var samples = _presenter.TelemetryFor(c?.ClientId);
        _dashboardTelemetry.UpdateClient(c, samples); _monitoringTelemetry.UpdateClient(c, samples);
        var enabled = c?.Connected == true && _runtime.IsRunning && !_closing;
        _dashboardPing.SetBusy(enabled && !state.PingPending, state.PingPending); _pingPage.SetBusy(enabled && !state.PingPending, state.PingPending);
        _scanner.SetAgent(c, enabled && !state.ScanPending, state.ScanPending); _dashboardScan.SetAgent(c, enabled && !state.ScanPending, state.ScanPending);
        _agentInfoButton.Enabled = enabled && !state.RemoteInfoPending;
        _chatButton.Enabled = enabled;
        _viewScreenButton.Enabled = enabled && c is not null &&
            !_runtime.IsScreenSharing(c.ClientId) && !_openingScreenClients.Contains(c.ClientId);
        _agentInfoButton.Text = state.RemoteInfoPending ? "Reading..." : "Get Agent Info";
        _agentInfoStatus.Text = state.RemoteInfoStatus;
        _agentInfoStatus.ForeColor = state.RemoteInfoError ? DashboardTheme.Red : DashboardTheme.Muted;
        _shutdownButton.Enabled = enabled && !state.PowerPending;
        _restartButton.Enabled = enabled && !state.PowerPending;
        _cancelPowerButton.Enabled = enabled && state.PowerPending;
        _powerCountdown.Enabled = enabled && !state.PowerPending;
        _powerStatus.Text = state.PowerStatus;
        _powerStatus.ForeColor = state.PowerError ? DashboardTheme.Red : DashboardTheme.Muted;
        _lockButton.Enabled = enabled && !state.LockPending;
        _unlockButton.Enabled = enabled && !state.LockPending;
        _lockMode.Enabled = enabled && !state.LockPending;
        _lockStatus.Text = state.LockStatus;
        _lockStatus.ForeColor = state.LockError ? DashboardTheme.Red : DashboardTheme.Muted;
        var key = c?.SessionId + string.Join(";", c?.Interfaces ?? []);
        if (_interfaceKey != key)
        {
            _interfaceKey = key; _interfaces.Rows.Clear();
            foreach (var nic in c?.Interfaces ?? []) _interfaces.Rows.Add(nic.Name, nic.IpAddress, nic.MacAddress, nic.Gateway, nic.Subnet, nic.Description);
            _interfaces.ClearSelection();
        }
        UpdatePingTargetChoices(c, state);
        var prefillKey = c is null ? "" : $"{c.SessionId}|{c.PrimaryInterface?.Subnet}";
        if (_prefillKey != prefillKey)
        {
            _prefillKey = prefillKey; _scanner.SubnetInput.Text = c?.PrimaryInterface?.Subnet ?? "";
        }
        _hosts.UpdateValue(state.Scan?.Devices.Count.ToString() ?? "--", state.ScanAt is DateTime at ? $"Selected session: {at:t}" : "No scan for selected session");
        _pingAverage.UpdateValue(state.Ping?.AverageResponseTimeMs is double ms ? $"{ms:0.#} ms" : "--",
            state.Ping is null ? "No ping for selected session" : $"Target: {state.Ping.Host}");
        _dashboardPing.SetResult(state.Ping); _pingPage.SetResult(state.Ping);
        PingStatus(state.PingStatus, state.PingError);
        RenderScan();
    }

    private void OnSecurityAlertReceived(SecurityAlertMessage alert)
    {
        lock (_alertSync)
        {
            _securityAlertHistory.Insert(0, alert);
            if (_securityAlertHistory.Count > 100)
                _securityAlertHistory.RemoveRange(100, _securityAlertHistory.Count - 100);
        }

        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(RenderSecurityMonitoring)); }
        catch (InvalidOperationException) { }
    }

    private void RenderSecurityMonitoring()
    {
        if (IsDisposed) return;
        var running = _runtime.IsRunning && !_closing;
        var enabled = _runtime.AutoDisconnectEnabled;
        _monitoringStatus.Text = running
            ? "Alert stream ready; Client monitoring publishes alerts over the authenticated session."
            : "Alert stream unavailable because the Server is stopped.";
        _monitoringStatus.ForeColor = running ? DashboardTheme.Blue : DashboardTheme.Red;
        _autoDisconnectStatus.Text = enabled
            ? "Enabled: qualifying Critical alerts will stop only their matching remote session."
            : "Disabled: alerts only; sessions are not stopped automatically.";
        _autoDisconnectStatus.ForeColor = enabled ? DashboardTheme.Red : DashboardTheme.Muted;
        _autoDisconnectButton.Enabled = running;
        _autoDisconnectButton.Text = enabled ? "Disable auto-disconnect" : "Enable auto-disconnect";

        SecurityAlertMessage[] alerts;
        lock (_alertSync) alerts = _securityAlertHistory.ToArray();
        _securityAlerts.BeginUpdate();
        try
        {
            _securityAlerts.Items.Clear();
            if (alerts.Length == 0)
            {
                _securityAlerts.Items.Add("No security alerts received in this Server run.");
                return;
            }

            foreach (var alert in alerts)
            {
                var session = alert.SessionId[..Math.Min(8, alert.SessionId.Length)];
                _securityAlerts.Items.Add(
                    $"[{alert.DetectionTimestampUtc.ToLocalTime():HH:mm:ss}] {alert.Severity} | " +
                    $"{alert.ProcessName} | session {session} | {alert.DetectionReason}");
            }
        }
        finally { _securityAlerts.EndUpdate(); }
    }

    private void ToggleAutoDisconnect()
    {
        if (_closing || !_runtime.IsRunning) return;
        var enable = !_runtime.AutoDisconnectEnabled;
        if (enable)
        {
            var confirmation = MessageBox.Show(this,
                "Enable automatic disconnect for qualifying security alerts?\n\n" +
                "Only the matching remote session is stopped. This policy can interrupt an active session.",
                "Confirm auto-disconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;
        }

        _autoDisconnectButton.Enabled = false;
        _autoDisconnectStatus.Text = "Updating security policy...";
        try
        {
            if (!_runtime.SetAutoDisconnectEnabled(enable))
            {
                _autoDisconnectStatus.Text = "Policy update rejected: administrator token is not configured.";
                _autoDisconnectStatus.ForeColor = DashboardTheme.Red;
                return;
            }

            RenderSecurityMonitoring();
        }
        catch (Exception ex)
        {
            _autoDisconnectStatus.Text = $"Policy update failed: {ex.GetBaseException().Message}";
            _autoDisconnectStatus.ForeColor = DashboardTheme.Red;
        }
    }

    private void UpdatePingTargetChoices(ClientSnapshot? client, SessionCommandState state)
    {
        var choices = new List<string>();
        if (client is not null)
        {
            choices.Add(client.IpAddress);
            choices.AddRange(client.Interfaces.SelectMany(networkInterface => new[]
            {
                networkInterface.IpAddress,
                networkInterface.Gateway
            }));
        }

        choices.AddRange(state.Scan?.Devices?.Select(device => device.IpAddress) ?? []);
        _dashboardPing.SetTargetChoices(choices);
        _pingPage.SetTargetChoices(choices);
    }
    private void RenderScan()
    {
        var state = _presenter.CommandsFor(Selected?.ClientId);
        var rows = state.Scan is null ? Array.Empty<ScanHostSnapshot>() : _runtime.MapScanResult(state.Scan, _snapshots);
        var summary = state.ScanStatus;
        if (state.Scan is not null && !state.ScanError)
            summary = $"{rows.Count} hosts found  |  {rows.Count(r => r.ManagedClient == "Yes")} managed hosts  |  Completed {state.ScanAt:t}\n{state.ScanOrigin}";
        _dashboardScan.SetResults(rows, summary, state.ScanError); _scanner.SetResults(rows, summary, state.ScanError);
    }
    private void PingStatus(string status, bool error = false)
    {
        _dashboardPing.SetStatus(status, error); _pingPage.SetStatus(status, error);
    }
    private async Task PingAsync()
    {
        var client = Selected;
        if (client?.Connected != true) return;
        var target = _pingPage.TargetInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            PingStatus("Please enter an IP address or hostname before pinging.", true);
            _pingPage.TargetInput.Focus();
            return;
        }

        await _presenter.PingAsync(client, target, _lifetime.Token, () => _closing, RenderSelection, RefreshState);
    }
    private async Task ScanAsync()
    {
        var client = Selected;
        if (client?.Connected != true) return;
        var subnet = _scanner.SubnetInput.Text;
        await _presenter.ScanAsync(client, subnet, (int)_scanner.TimeoutInput.Value,
            _lifetime.Token, () => _closing, RenderSelection, RefreshState);
    }
    private Task OpenChatAsync()
    {
        var client = Selected;
        if (client is null || !client.Connected) return Task.CompletedTask;
        IRemoteChatSession? chat = null;
        try
        {
            chat = _runtime.OpenChatSession(client.ClientId, client.SessionId);
            var form = new RemoteChatForm(client.Hostname, chat.SendAsync);
            Action<RemoteChatMessage> onMessage = form.AppendMessage;
            chat.MessageReceived += onMessage;
            form.FormClosed += (_, _) =>
            {
                chat.MessageReceived -= onMessage;
                _ = chat.DisposeAsync();
            };
            form.Show(this);
        }
        catch (Exception ex)
        {
            if (chat is not null) _ = chat.DisposeAsync();
            MessageBox.Show(this, $"Unable to open chat: {ex.GetBaseException().Message}",
                "NetworkAdmin Chat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return Task.CompletedTask;
    }

    private void OpenScreenViewer()
    {
        var client = Selected;
        if (client?.Connected != true || !_openingScreenClients.Add(client.ClientId)) return;
        var sessionId = client.SessionId;
        var form = new RemoteViewerForm(client.Hostname, client.ClientId,
            (token, onConnecting) => _runtime.RequestScreenAsync(client.ClientId, sessionId, token, onConnecting));
        form.FormClosed += (_, _) =>
        {
            _openingScreenClients.Remove(client.ClientId);
            RefreshState();
        };
        form.Show(this);
        RenderSelection();
    }

    private async Task GetAgentInfoAsync()
    {
        var client = Selected;
        if (client?.Connected != true) return;
        await _presenter.GetAgentInfoAsync(client, _lifetime.Token, () => _closing,
            RenderSelection, RefreshState);
    }
    private async Task PowerAsync(RemoteAdministrationCommandType command)
    {
        var client = Selected;
        if (client?.Connected != true) return;
        var state = _presenter.CommandsFor(client.ClientId);
        if (state.PowerPending) return;

        var action = command == RemoteAdministrationCommandType.Restart ? "restart" : "shut down";
        var countdown = (int)_powerCountdown.Value;
        var confirmation = MessageBox.Show(this,
            $"Are you sure you want to {action} '{client.Hostname}'?\n\n"
            + $"The command will start after {countdown} seconds. Applications will not be force-closed.",
            $"Confirm {action}", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes) return;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _powerCancellation = cancellation;
        try
        {
            await _presenter.PowerAsync(client, command, countdown, cancellation.Token, () => _closing,
                RenderSelection, RefreshState, UpdatePowerProgress);
        }
        finally
        {
            if (ReferenceEquals(_powerCancellation, cancellation)) _powerCancellation = null;
        }
    }

    private void UpdatePowerProgress(string clientId, string sessionId, SessionCommandState state,
        RemoteAdministrationResultMessage result)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_closing || !_presenter.IsCurrentSession(clientId, sessionId, state)) return;
                state.PowerError = result.Status is RemoteAdministrationCommandStatus.Rejected
                    or RemoteAdministrationCommandStatus.Failed or RemoteAdministrationCommandStatus.Timeout;
                state.PowerStatus = result.Status switch
                {
                    RemoteAdministrationCommandStatus.Accepted when result.ErrorMessage == "Command sent to client."
                        => "Command sent to client; waiting for acceptance...",
                    RemoteAdministrationCommandStatus.Accepted => "Client accepted the command; waiting to prepare...",
                    RemoteAdministrationCommandStatus.Preparing => "Client accepted the command and is preparing Windows power action...",
                    _ => ServerDashboardPresenter.FormatPowerResult("power", result)
                };
                RenderSelection();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private async Task LockAsync(bool lockClient)
    {
        var client = Selected;
        if (client?.Connected != true) return;
        var state = _presenter.CommandsFor(client.ClientId);
        if (state.LockPending) return;

        var managed = _lockMode.SelectedIndex == 1;
        var action = lockClient ? "lock" : "unlock";
        var mode = managed ? "Managed Workstation Lock" : "Windows Lock";
        var confirmation = MessageBox.Show(this,
            $"Are you sure you want to {action} '{client.Hostname}' using {mode}?",
            $"Confirm {action}", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes) return;

        var command = managed
            ? (lockClient ? RemoteAdministrationCommandType.ManagedWorkstationLock : RemoteAdministrationCommandType.ManagedWorkstationUnlock)
            : (lockClient ? RemoteAdministrationCommandType.WindowsLock : RemoteAdministrationCommandType.WindowsUnlock);
        var sessionId = client.SessionId;
        state.LockPending = true;
        state.LockError = false;
        state.LockStatus = $"Sending {mode} {action} command to {client.Hostname}...";
        RenderSelection();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _lockCancellation = cancellation;
        try
        {
            var result = await _runtime.SendRemoteAdministrationAsync(
                client.ClientId,
                command,
                cancellation.Token,
                progress: update => UpdateLockProgress(client.ClientId, sessionId, state, update));
            if (_closing || !_presenter.IsCurrentSession(client.ClientId, sessionId, state)) return;
            state.LockError = result.Status != RemoteAdministrationCommandStatus.Completed;
            state.LockStatus = FormatLockResult(result);
        }
        catch (Exception ex)
        {
            if (!_closing && _presenter.IsCurrentSession(client.ClientId, sessionId, state))
            {
                state.LockError = true;
                state.LockStatus = $"{mode} {action} failed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_lockCancellation, cancellation)) _lockCancellation = null;
            if (state.SessionId == sessionId) state.LockPending = false;
            if (!_closing) RefreshState();
        }
    }

    private void UpdateLockProgress(string clientId, string sessionId, SessionCommandState state,
        RemoteAdministrationResultMessage result)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_closing || !_presenter.IsCurrentSession(clientId, sessionId, state)) return;
                state.LockError = result.Status is RemoteAdministrationCommandStatus.Rejected
                    or RemoteAdministrationCommandStatus.Failed or RemoteAdministrationCommandStatus.Timeout;
                state.LockStatus = result.Status switch
                {
                    RemoteAdministrationCommandStatus.Accepted when result.ErrorMessage == "Command sent to client."
                        => "Command sent to client; waiting for acceptance...",
                    RemoteAdministrationCommandStatus.Accepted => "Client accepted the command; waiting to prepare...",
                    RemoteAdministrationCommandStatus.Preparing => "Client accepted the command and is preparing the lock action...",
                    _ => FormatLockResult(result)
                };
                RenderSelection();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static string FormatLockResult(RemoteAdministrationResultMessage result) =>
        result.Status == RemoteAdministrationCommandStatus.Completed
            ? result.Result
            : $"Lock command {result.Status}: {result.ErrorMessage}";

    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _refresh.Stop(); Enabled = false;
        _runtime.SecurityAlertReceived -= OnSecurityAlertReceived;
        try
        {
            _powerCancellation?.Cancel();
            _lockCancellation?.Cancel();
            await _lifetime.CancelAsync(); await _runtime.StopAsync();
            await Task.WhenAll(_pingTask ?? Task.CompletedTask, _scanTask ?? Task.CompletedTask,
                _agentInfoTask ?? Task.CompletedTask, _powerTask ?? Task.CompletedTask,
                _lockTask ?? Task.CompletedTask);
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
