namespace NetworkAdmin.Server.UI;

internal sealed class ClientDetailView : DashboardCard
{
    private readonly Label _hostname = DashboardTheme.Label("No client selected", 17, true);
    private readonly Label _identity = DashboardTheme.Label("Select a connected agent above or on Clients.", 9, color: DashboardTheme.Muted);
    private readonly Label _badge = DashboardTheme.Label("--", 9, true, DashboardTheme.Green);
    private readonly Dictionary<string, Label> _values = new();
    private readonly ToolTip _tips = new();
    public ClientDetailView() : base("Selected Client")
    {
        var columns = DashboardTheme.Table(2, 1);
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var left = DashboardTheme.Rows(36, 24, 27, -100);
        left.Padding = new Padding(0, 0, 14, 0);
        left.Controls.Add(_hostname, 0, 0); left.Controls.Add(_identity, 0, 1); left.Controls.Add(_badge, 0, 2);
        left.Controls.Add(Fields("Client ID", "Session ID", "Connected", "Last seen"), 0, 3);
        var right = DashboardTheme.Rows(30, -100);
        right.BackColor = DashboardTheme.Input; right.Padding = new Padding(10, 0, 6, 3);
        right.Controls.Add(DashboardTheme.Label("Network Information", 10, true), 0, 0);
        right.Controls.Add(Fields("Interface", "IPv4", "MAC", "Gateway", "Subnet"), 0, 1);
        columns.Controls.Add(left, 0, 0); columns.Controls.Add(right, 1, 0);
        Body.Controls.Add(columns);
    }
    private TableLayoutPanel Fields(params string[] names)
    {
        var table = DashboardTheme.Table(2, names.Length);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < names.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / names.Length));
            table.Controls.Add(DashboardTheme.Label(names[i], 8, color: DashboardTheme.Muted), 0, i);
            var value = DashboardTheme.Label("--", names[i].EndsWith("ID") ? 8 : 9);
            _values[names[i]] = value; table.Controls.Add(value, 1, i);
        }
        return table;
    }
    public void UpdateClient(ClientSnapshot? c)
    {
        _hostname.Text = c?.Hostname ?? "No client selected";
        _tips.SetToolTip(_hostname, _hostname.Text);
        _identity.Text = c is null ? "Choose a managed agent to view its details." : $"{c.IpAddress}  |  {c.PrimaryInterface?.Name ?? "TCP peer address"}";
        _badge.Text = c is null ? "Waiting for selection" : c.Connected ? "\u25CF  Online" : "\u25CB  Offline / stale data";
        _badge.ForeColor = c?.Connected == true ? DashboardTheme.Green : DashboardTheme.Red;
        Set("Client ID", c?.ClientId); Set("Session ID", c?.SessionId);
        Set("Connected", c?.ConnectedAt.ToLocalTime().ToString("g")); Set("Last seen", c?.LastSeen.ToLocalTime().ToString("G"));
        var nic = c?.PrimaryInterface;
        Set("Interface", nic?.Name); Set("IPv4", c?.IpAddress); Set("MAC", nic?.MacAddress);
        Set("Gateway", nic?.Gateway); Set("Subnet", nic?.Subnet);
    }
    private void Set(string name, string? value)
    {
        _values[name].Text = DashboardTheme.Value(value);
        _tips.SetToolTip(_values[name], _values[name].Text);
    }
    protected override void Dispose(bool disposing) { if (disposing) _tips.Dispose(); base.Dispose(disposing); }
}
