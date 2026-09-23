using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server;

internal sealed class ServerDashboardForm : Form
{
    private readonly ServerRuntime _runtime;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _selector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _server = TextLabel();
    private readonly Label _summary = TextLabel();
    private readonly Label _details = TextLabel();
    private readonly Label _monitor = TextLabel();
    private readonly Label _cpuLabel = TextLabel();
    private readonly Label _ramLabel = TextLabel();
    private readonly ProgressBar _cpu = new() { Dock = DockStyle.Fill };
    private readonly ProgressBar _ram = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _clients = Grid();
    private readonly DataGridView _interfaces = Grid();
    private readonly DataGridView _scanGrid = Grid();
    private readonly TextBox _subnet = new() { Width = 260, PlaceholderText = "192.168.1.0/24" };
    private readonly TextBox _target = new() { Width = 260, PlaceholderText = "IP address or hostname" };
    private readonly Button _scan = ActionButton("Scan Network");
    private readonly Button _ping = ActionButton("Ping (4 requests)");
    private readonly Label _scanStatus = TextLabel();
    private readonly TextBox _pingResult = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private IReadOnlyList<ClientSnapshot> _snapshots = [];
    private ScanResultMessage? _lastScan;
    private string _lastScanDescription = "No scan completed.";
    private string? _selectedId;
    private string _selectorKey = "";
    private string _scanKey = "";
    private bool _updating;
    private bool _scanning;
    private bool _pinging;
    private bool _closing;
    private bool _closed;
    private string? _prefillSession;
    private Task? _pingTask;
    private Task? _scanTask;

    public ServerDashboardForm(ServerRuntime runtime)
    {
        _runtime = runtime;
        Text = "Network Administration | Server";
        Size = new Size(1180, 820);
        MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(243, 246, 250);
        var root = CreateLayout(4);
        root.Padding = new Padding(20);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = TextLabel();
        title.Text = "NETWORK ADMINISTRATION";
        title.Font = new Font(Font.FontFamily, 20, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(24, 49, 80);
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_server, 0, 1);
        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var selectedLabel = TextLabel(); selectedLabel.Text = "Selected client / agent";
        selection.Controls.Add(selectedLabel, 0, 0); selection.Controls.Add(_selector, 1, 0);
        root.Controls.Add(selection, 0, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(20, 10) };
        root.Controls.Add(tabs, 0, 3);
        Controls.Add(root);

        Page(tabs, "Dashboard").Controls.Add(_summary);
        _summary.Font = new Font(Font.FontFamily, 14);

        var clientsPage = CreateLayout(3);
        clientsPage.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        clientsPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        clientsPage.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        clientsPage.Controls.Add(_clients, 0, 0);
        clientsPage.Controls.Add(_details, 0, 1);
        clientsPage.Controls.Add(_interfaces, 0, 2);
        Page(tabs, "Clients").Controls.Add(clientsPage);
        foreach (var heading in new[] { "Hostname", "IPv4", "MAC", "Status", "SessionId", "ClientId", "Connected time", "Last seen" })
            _clients.Columns.Add(heading, heading);

        var scanner = CreateLayout(3);
        scanner.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        scanner.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        scanner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        scanner.Controls.Add(Toolbar(_subnet, _scan), 0, 0);
        _scanStatus.Text = "Scan from the selected agent. Enter a unicast /24â€“/30 subnet.\nDiscovered hosts are managed only when an agent is connected.";
        scanner.Controls.Add(_scanStatus, 0, 1);
        scanner.Controls.Add(_scanGrid, 0, 2);
        Page(tabs, "IP Scanner").Controls.Add(scanner);

        var pingPage = CreateLayout(2);
        pingPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        pingPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pingPage.Controls.Add(Toolbar(_target, _ping), 0, 0);
        _pingResult.Text = "Select the managed client above, enter a target, then press Ping.\r\nThe selected agent sends four ICMP Echo Requests.";
        pingPage.Controls.Add(_pingResult, 0, 1);
        Page(tabs, "Ping").Controls.Add(pingPage);

        var monitoring = CreateLayout(6);
        foreach (var height in new[] { 120, 40, 32, 40, 32 }) monitoring.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        monitoring.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitoring.Controls.Add(_monitor, 0, 0);
        monitoring.Controls.Add(_cpuLabel, 0, 1); monitoring.Controls.Add(_cpu, 0, 2);
        monitoring.Controls.Add(_ramLabel, 0, 3); monitoring.Controls.Add(_ram, 0, 4);
        Page(tabs, "Monitoring").Controls.Add(monitoring);

        _selector.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            _selectedId = (_selector.SelectedItem as ClientSnapshot)?.SessionId;
            ShowSelected();
        };
        _clients.SelectionChanged += (_, _) =>
        {
            if (_updating || _clients.CurrentRow?.Tag is not string id) return;
            _selectedId = id;
            _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => c.SessionId == id);
            ShowSelected();
        };
        _ping.Click += (_, _) => _pingTask = PingAsync();
        _scan.Click += (_, _) => _scanTask = ScanAsync();
        _refresh.Tick += (_, _) => RefreshState();
        Shown += (_, _) => { _runtime.Start(); RefreshState(); _refresh.Start(); };
        FormClosing += CloseAsync;
    }

    private ClientSnapshot? Selected => _snapshots.FirstOrDefault(c => c.SessionId == _selectedId);

    private void RefreshState()
    {
        if (_closing) return;
        _snapshots = _runtime.GetClients();
        _server.Text = $"Server: {_runtime.Status}    |    Listening address: {_runtime.Endpoint}    |    Connected agents: {_snapshots.Count(c => c.Connected)}";
        _updating = true;
        try
        {
            var key = string.Join(";", _snapshots.Select(c => c.ToString()));
            if (key != _selectorKey)
            {
                _selectorKey = key;
                _selector.Items.Clear();
                _selector.Items.AddRange(_snapshots.Cast<object>().ToArray());
                _selector.SelectedItem = _selector.Items.Cast<ClientSnapshot>().FirstOrDefault(c => c.SessionId == _selectedId);
            }
            var ids = _snapshots.Select(c => c.SessionId).ToHashSet();
            foreach (var row in _clients.Rows.Cast<DataGridViewRow>().Where(r => !ids.Contains((string)r.Tag!)).ToArray())
                _clients.Rows.Remove(row);
            foreach (var client in _snapshots)
            {
                var row = _clients.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (string?)r.Tag == client.SessionId);
                if (row is null) { row = _clients.Rows[_clients.Rows.Add()]; row.Tag = client.SessionId; }
                row.SetValues(client.Hostname, client.IpAddress, client.MacAddress,
                    client.Connected ? "Connected" : "Disconnected", client.SessionId, client.ClientId,
                    client.ConnectedAt.ToLocalTime(), client.LastSeen.ToLocalTime());
                row.DefaultCellStyle.ForeColor = client.Connected ? Color.FromArgb(24, 49, 80) : Color.DimGray;
                row.Selected = client.SessionId == _selectedId;
            }
        }
        finally { _updating = false; }
        ShowSelected();
        if (_lastScan is not null)
        {
            var rows = ServerRuntime.MatchScan(_lastScan, _snapshots);
            var key = string.Join(";", rows);
            if (key != _scanKey)
            {
                _scanKey = key; _scanGrid.DataSource = rows.ToList();
                SetHeaders(_scanGrid, "IP Address", "MAC Address", "Status", "Managed Client", "Hostname (matched)");
            }
        }
    }

    private void ShowSelected()
    {
        var c = Selected;
        var live = c?.Connected == true;
        _ping.Enabled = live && !_pinging && !_closing && _runtime.IsRunning;
        _scan.Enabled = live && !_scanning && !_closing && _runtime.IsRunning;
        var state = c is null ? "No client selected. Select an agent above or on the Clients page."
            : !live ? "Disconnected â€” monitoring is no longer live. Values below are the last received sample."
            : c.LastTelemetryAt is null ? "Waiting for client telemetry."
            : DateTime.UtcNow - c.LastTelemetryAt > TimeSpan.FromSeconds(10) ? "Telemetry is stale â€” waiting for a fresh sample." : "Live client telemetry";
        var identity = c is null ? "" : $"{c.Hostname}  |  {c.IpAddress}\nSessionId: {c.SessionId}\nClientId: {c.ClientId}";
        var cpu = c?.Cpu is float cp ? $"{cp:0.0}%" : "â€”";
        var ram = c?.Ram is float rp ? $"{rp:0.0}%" : "â€”";
        var updated = c?.LastTelemetryAt?.ToLocalTime().ToString("G") ?? "No telemetry received";
        _summary.Text = $"Server: {_runtime.Status}\nManaged clients: {_snapshots.Count(x => x.Connected)}\n\nSelected client: {identity}\n\n{state}\nCPU: {cpu}     RAM: {ram}\nLast telemetry update: {updated}\n\nLast scan: {_lastScanDescription}";
        _details.Text = c is null ? state : $"{identity}\n{state}\nNetwork interfaces reported by this client:";
        _monitor.Text = $"{identity}\n{state}\nLast telemetry update: {updated}";
        _cpuLabel.Text = $"CPU: {cpu}"; _ramLabel.Text = $"RAM: {ram}";
        _cpu.Value = (int)Math.Clamp(c?.Cpu ?? 0, 0, 100);
        _ram.Value = (int)Math.Clamp(c?.Ram ?? 0, 0, 100);
        _cpu.Enabled = _ram.Enabled = live;
        var interfaceRows = c?.Interfaces.ToList() ?? [];
        if (_interfaces.Tag as string != string.Join(";", interfaceRows) + _selectedId)
        {
            _interfaces.Tag = string.Join(";", interfaceRows) + _selectedId;
            _interfaces.DataSource = interfaceRows;
            SetHeaders(_interfaces, "Interface", "IPv4", "MAC", "Gateway", "Subnet / network");
        }
        if (c is not null && _prefillSession != c.SessionId && c.Interfaces.Count > 0)
        {
            _prefillSession = c.SessionId;
            _subnet.Text = c.Interfaces[0].Subnet;
        }
    }

    private async Task PingAsync()
    {
        var client = Selected;
        if (client?.Connected != true || _pinging) return;
        _pinging = true; ShowSelected();
        var origin = $"Agent: {client.Hostname} ({client.IpAddress}) | SessionId: {client.SessionId}";
        _pingResult.Text = $"{origin}\r\nPinging {_target.Text.Trim()}â€¦";
        try
        {
            var result = await _runtime.PingAsync(client.SessionId, _target.Text, _lifetime.Token);
            _pingResult.Text = $"{origin}\r\nTarget: {result.Host}\r\nSuccess: {result.Success}\r\nStatus: {result.StatusMessage}\r\n" +
                (result.Sent == 0 ? "This agent did not report packet statistics; update the agent.\r\n" :
                $"Sent: {result.Sent}   Received: {result.Received}   Lost: {result.Lost}\r\nPacket loss: {result.PacketLossPercent:0.#}%\r\nAverage response time: {result.AverageResponseTimeMs?.ToString("0.##") ?? "â€”"} ms\r\n") +
                string.Join("\r\n", (result.Attempts ?? []).Select(a => $"Attempt {a.Number}: {a.Status} | {a.ResponseTimeMs?.ToString() ?? "â€”"} ms"));
        }
        catch (Exception ex) { if (!_closing) _pingResult.Text = $"{origin}\r\nPing failed: {ex.Message}"; }
        finally { _pinging = false; if (!_closing) ShowSelected(); }
    }

    private async Task ScanAsync()
    {
        var client = Selected;
        if (client?.Connected != true || _scanning) return;
        _scanning = true; ShowSelected();
        var origin = $"{client.Hostname} | SessionId: {client.SessionId}";
        _scanStatus.Text = $"Scanning from {origin}â€¦\nAny table below belongs to the last completed scan.";
        try
        {
            var result = await _runtime.ScanAsync(client.SessionId, _subnet.Text, _lifetime.Token);
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
            _lastScan = result;
            _scanKey = "force refresh";
            _lastScanDescription = $"{result.Devices.Count} hosts in {result.Subnet} at {DateTime.Now:t}\nAgent: {origin}";
            _scanStatus.Text = _lastScanDescription + "\nManaged = currently connected agent. Other hosts have no CPU/RAM telemetry.";
            RefreshState();
        }
        catch (Exception ex) { if (!_closing) _scanStatus.Text = $"Scan failed: {ex.Message}\nPrevious completed results, if any, remain below."; }
        finally { _scanning = false; if (!_closing) ShowSelected(); }
    }

    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _refresh.Stop(); Enabled = false;
        try
        {
            await _lifetime.CancelAsync();
            await _runtime.StopAsync();
            await Task.WhenAll(_pingTask ?? Task.CompletedTask, _scanTask ?? Task.CompletedTask);
        }
        catch (Exception ex) { Console.WriteLine($"Server shutdown: {ex.Message}"); }
        finally
        {
            _refresh.Dispose(); _lifetime.Dispose(); _closed = true; Close();
        }
    }

    private static Label TextLabel() => new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(4), AutoEllipsis = true };
    private static Button ActionButton(string text) => new() { Text = text, AutoSize = true, Height = 34, BackColor = Color.FromArgb(24, 74, 126), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private static TableLayoutPanel CreateLayout(int rows) => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows };
    private static FlowLayoutPanel Toolbar(TextBox input, Button button)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), WrapContents = false };
        panel.Controls.Add(input); panel.Controls.Add(button); return panel;
    }
    private static TabPage Page(TabControl tabs, string title)
    {
        var page = new TabPage(title) { Padding = new Padding(14), BackColor = Color.White }; tabs.TabPages.Add(page); return page;
    }
    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoGenerateColumns = true
    };
    private static void SetHeaders(DataGridView grid, params string[] headers)
    {
        for (var i = 0; i < Math.Min(grid.Columns.Count, headers.Length); i++) grid.Columns[i].HeaderText = headers[i];
    }
}
