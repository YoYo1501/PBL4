using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Server.UI;

internal sealed class ScanView : DashboardCard
{
    public Button ScanButton { get; } = DashboardTheme.Button("Scan Now");
    public TextBox SubnetInput { get; } = DashboardTheme.InputBox("192.168.1.0/24");
    public NumericUpDown TimeoutInput { get; } = new()
    {
        Minimum = NetworkAdmin.Shared.Protocol.ScanSubnet.MinTimeoutMs,
        Maximum = NetworkAdmin.Shared.Protocol.ScanSubnet.MaxTimeoutMs,
        Value = NetworkAdmin.Shared.Protocol.ScanSubnet.DefaultTimeoutMs,
        Increment = 50,
        DecimalPlaces = 0,
        ThousandsSeparator = false,
        Dock = DockStyle.Fill,
        BackColor = DashboardTheme.Input,
        ForeColor = DashboardTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10),
        Margin = new Padding(4, 8, 8, 4)
    };
    private readonly DataGridView _grid = DashboardTheme.Grid(("IpAddress", "IP Address", 115),
        ("Hostname", "Hostname", 125), ("MacAddress", "MAC Address", 140), ("Status", "Status", 65), ("ManagedClient", "Managed", 65));
    private readonly Label _status = DashboardTheme.Label("No scan completed.", 9, color: DashboardTheme.Muted);
    private readonly Label _agent = DashboardTheme.Label("Scan from the globally selected agent", 9, color: DashboardTheme.Muted);
    private string _rowKey = "";
    public ScanView(bool dedicated) : base(dedicated ? "IP Scanner / Remote Network Discovery" : "Recent Network Scan")
    {
        var layout = DashboardTheme.Rows(40, -100, 52);
        var toolbar = DashboardTheme.Table(dedicated ? 4 : 3, 1);
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dedicated ? 30 : 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, dedicated ? 50 : 0));
        if (dedicated)
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        toolbar.Controls.Add(_agent, 0, 0);
        if (dedicated)
        {
            toolbar.Controls.Add(SubnetInput, 1, 0);
            toolbar.Controls.Add(TimeoutInput, 2, 0);
            ScanButton.Text = "Scan Network";
        }
        toolbar.Controls.Add(ScanButton, dedicated ? 3 : 2, 0);
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(_grid, 0, 1); layout.Controls.Add(_status, 0, 2);
        Body.Controls.Add(layout);
    }
    public void SetAgent(ClientSnapshot? c, bool enabled, bool busy)
    {
        _agent.Text = c is null ? "Select a managed client first" : $"Agent: {c.Hostname}  |  {c.IpAddress}";
        ScanButton.Enabled = enabled; ScanButton.Text = busy ? "Scanning..." : SubnetInput.Parent is null ? "Scan Now" : "Scan Network";
        SubnetInput.Enabled = !busy;
        TimeoutInput.Enabled = !busy;
    }
    public void SetResults(IReadOnlyList<ScanHostSnapshot> rows, string status, bool error = false)
    {
        _status.Text = status; _status.ForeColor = error ? DashboardTheme.Red : DashboardTheme.Muted;
        var key = string.Join(";", rows);
        if (_rowKey == key) return;
        _rowKey = key; _grid.Rows.Clear();
        foreach (var row in rows)
        {
            var index = _grid.Rows.Add(row.IpAddress, DashboardTheme.Value(row.Hostname), DashboardTheme.Value(row.MacAddress), row.Status, row.ManagedClient);
            _grid.Rows[index].Cells[3].Style.ForeColor = row.Status == "Online" ? DashboardTheme.Green : DashboardTheme.Muted;
            if (row.ManagedClient == "Yes") _grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(8, 53, 90);
        }
        _grid.ClearSelection();
    }
}

internal sealed class PingView : DashboardCard
{
    public ComboBox TargetInput { get; } = CreateTargetInput();
    public Button PingButton { get; } = DashboardTheme.Button("Ping (4 requests)");
    private readonly Label[] _metrics = new Label[5];
    private string _targetItemsKey = string.Empty;
    private PingResult? _shownResult;
    private readonly Label _status = DashboardTheme.Label("Select an agent, enter a target, then Ping.", 9, color: DashboardTheme.Muted);
    private readonly DataGridView _attempts = DashboardTheme.Grid(("Number", "#", 35), ("Status", "Status", 180), ("ResponseTime", "Response Time", 100));
    public PingView(bool dedicated) : base(dedicated ? "Ping Tool / Remote ICMP Diagnostics" : "Recent Ping Results")
    {
        var layout = DashboardTheme.Rows(38, 65, -100, 44);
        var toolbar = DashboardTheme.Table(3, 1);
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        toolbar.Controls.Add(DashboardTheme.Label("Target:", 9), 0, 0);
        toolbar.Controls.Add(TargetInput, 1, 0); toolbar.Controls.Add(PingButton, 2, 0);
        layout.Controls.Add(toolbar, 0, 0);
        var metrics = DashboardTheme.Table(5, 1);
        var titles = new[] { "Sent", "Received", "Lost", "Packet Loss", "Average RTT" };
        for (var i = 0; i < 5; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            var cell = DashboardTheme.Rows(22, -100); cell.BackColor = DashboardTheme.Input; cell.Margin = new Padding(2, 2, 2, 8); cell.Padding = new Padding(5, 0, 0, 0);
            cell.Controls.Add(DashboardTheme.Label(titles[i], 8), 0, 0);
            _metrics[i] = DashboardTheme.Label("--", 15, true, i == 2 ? DashboardTheme.Red : i == 1 ? DashboardTheme.Green : DashboardTheme.Text);
            cell.Controls.Add(_metrics[i], 0, 1); metrics.Controls.Add(cell, i, 0);
        }
        layout.Controls.Add(metrics, 0, 1); layout.Controls.Add(_attempts, 0, 2); layout.Controls.Add(_status, 0, 3);
        Body.Controls.Add(layout);
    }

    public void SetTargetChoices(IEnumerable<string> choices)
    {
        var values = choices
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var key = string.Join(";", values);
        if (key == _targetItemsKey)
            return;

        var current = TargetInput.Text;
        _targetItemsKey = key;
        TargetInput.BeginUpdate();
        try
        {
            TargetInput.Items.Clear();
            TargetInput.Items.AddRange(values.Cast<object>().ToArray());
            TargetInput.Text = current;
        }
        finally
        {
            TargetInput.EndUpdate();
        }
    }

    private static ComboBox CreateTargetInput() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = DashboardTheme.Input,
        ForeColor = DashboardTheme.Text,
        FlatStyle = FlatStyle.Flat,
        DropDownStyle = ComboBoxStyle.DropDown,
        Font = new Font("Segoe UI", 11),
        Margin = new Padding(4, 8, 8, 4),
        AccessibleName = "Ping target IP address or hostname"
    };
    public void SetBusy(bool enabled, bool busy)
    {
        PingButton.Enabled = enabled; PingButton.Text = busy ? "Pinging..." : "Ping (4 requests)"; TargetInput.Enabled = !busy;
    }
    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text; _status.ForeColor = error ? DashboardTheme.Red : DashboardTheme.Muted;
    }
    public void SetResult(PingResult? result)
    {
        if (ReferenceEquals(_shownResult, result)) return;
        _shownResult = result;
        if (result is null)
        {
            foreach (var metric in _metrics) metric.Text = "--";
            _attempts.Rows.Clear();
            return;
        }
        var reported = result.Sent > 0;
        string[] values = [reported ? result.Sent.ToString() : "--", reported ? result.Received.ToString() : "--",
            reported ? result.Lost.ToString() : "--", reported ? $"{result.PacketLossPercent:0.#}%" : "--",
            result.AverageResponseTimeMs is double ms ? $"{ms:0.#} ms" : "--"];
        for (var i = 0; i < values.Length; i++) _metrics[i].Text = values[i];
        _attempts.Rows.Clear();
        foreach (var attempt in result.Attempts ?? [])
        {
            if (attempt is null) continue;
            var index = _attempts.Rows.Add(attempt.Number, attempt.Status, attempt.ResponseTimeMs is long rtt ? $"{rtt} ms" : "--");
            _attempts.Rows[index].Cells[1].Style.ForeColor = attempt.Success ? DashboardTheme.Green : DashboardTheme.Red;
        }
        _attempts.ClearSelection();
    }
}
