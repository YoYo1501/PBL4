using System.Net;
using System.Reflection;
using NetworkAdmin.Server;
using NetworkAdmin.Server.UI;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Models;

internal static class LayoutValidation
{
    [STAThread]
    private static void Main(string[] args)
    {
        var baseline = args.Contains("--96");
        Application.SetHighDpiMode(baseline ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        ValidateSessionSelection();
        var output = Path.GetFullPath("artifacts/layout");
        Directory.CreateDirectory(output);
        var sizes = baseline ? new[] { new Size(1366, 768), new Size(1920, 1080) }
            : new[] { new Size(2732, 1536), new Size(3200, 1904) };
        foreach (var size in sizes)
        {
            using var form = new ServerDashboardForm(new ServerRuntime(new ServerOptions { Host = IPAddress.Loopback, Port = 0 }));
            _ = form.Handle;
            form.ClientSize = size;
            form.PerformAutoScale();
            form.PerformLayout();
            typeof(ServerDashboardForm).GetMethod("RefreshState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            // Render the actual controls off-screen; do not show the app or start networking.
            using var root = form.Controls[0];
            form.Controls.Remove(root);
            root.Dock = DockStyle.None;
            root.Size = size;
            root.BackColor = DashboardTheme.Background;
            root.CreateControl();
            root.PerformLayout();
            foreach (var control in Descendants(root)) control.PerformLayout();
            using var bitmap = new Bitmap(size.Width, size.Height);
            root.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            var path = Path.Combine(output, $"dashboard-{size.Width}-dpi{form.DeviceDpi}.png");
            bitmap.Save(path);
            var controls = Descendants(root).Where(c => c.Visible).ToArray();
            var title = controls.OfType<Label>().Single(l => l.Text == "Network Administration");
            var nav = controls.OfType<Button>().Single(b => b.AccessibleName == "Dashboard");
            if (title.Height < title.Font.Height || nav.Height < nav.Font.Height + 4 ||
                title.Width < TextRenderer.MeasureText(title.Text, title.Font).Width)
                throw new Exception("Clipped title or navigation at " + size);
            foreach (var card in controls.OfType<DashboardCard>())
                if (card.Bottom > card.Parent!.ClientSize.Height + 1)
                    throw new Exception("Card overflows its allocated row: " + card.Heading.Text);
            Console.WriteLine($"PASS {size.Width}x{size.Height}, DPI={form.DeviceDpi}: title, navigation and card bounds. Render: {path}");
        }
    }
    private static void ValidateSessionSelection()
    {
        using var form = new ServerDashboardForm(new ServerRuntime(new ServerOptions()));
        _ = form.Handle;
        var now = DateTime.UtcNow;
        var a = new ClientSnapshot("session-A", "A", "Agent A", "10.0.0.1", true, now, now, now, 10, 20, []);
        var b = a with { SessionId = "session-B", ClientId = "B", Hostname = "Agent B" };
        SetField(form, "_snapshots", new ClientSnapshot[] { a, b });
        var states = Field<Dictionary<string, SessionCommandState>>(form, "_commands");
        var pendingA = new SessionCommandState { PingStatus = "Pending A", ScanStatus = "Pending A" };
        states[a.ClientId] = pendingA;
        Field<ServerDashboardPresenter>(form, "_presenter").ObserveClients([a, b]);
        Select(a.ClientId);
        Select(b.ClientId);
        // Model completion of the captured A state after global selection changed to B.
        pendingA.Ping = new PingResult { Host = "A-target", Sent = 4, Received = 4 };
        pendingA.Scan = new ScanResultMessage { Success = true, Devices = [new() { IpAddress = "10.0.0.99", IsOnline = true }] };
        pendingA.PingStatus = "Completed A";
        Select(b.ClientId);
        var pingView = Field<PingView>(form, "_pingPage");
        var scanView = Field<ScanView>(form, "_scanner");
        if (Field<PingResult?>(pingView, "_shownResult") is not null || Field<DataGridView>(scanView, "_grid").Rows.Count != 0)
            throw new Exception("A results leaked into selected B");
        Console.WriteLine("PASS Phase 11 UI: completion for A stays hidden while B is selected");
        Select(a.ClientId);
        if (!ReferenceEquals(Field<PingResult?>(pingView, "_shownResult"), pendingA.Ping)
            || Field<DataGridView>(scanView, "_grid").Rows.Count != 1)
            throw new Exception("A results were not restored when selecting A");
        Console.WriteLine("PASS Phase 11 UI: selecting A restores only A Ping and Scan results");
        var reconnected = a with { SessionId = "new-session-A" };
        SetField(form, "_snapshots", new ClientSnapshot[] { reconnected, b });
        Field<ServerDashboardPresenter>(form, "_presenter").ObserveClients([reconnected, b]);
        Select(reconnected.ClientId);
        if (!ReferenceEquals(Field<PingResult?>(pingView, "_shownResult"), pendingA.Ping)
            || Field<DataGridView>(scanView, "_grid").Rows.Count != 1)
            throw new Exception("Command results were not retained for the reconnecting ClientId");
        Console.WriteLine("PASS Phase 11 UI: same ClientId retains its last Ping and Scan results after reconnect");
        reconnected = reconnected with { Interfaces = [new("Wi-Fi", "172.31.99.116", "", "172.31.98.1", "172.31.98.0/23")] };
        SetField(form, "_snapshots", new ClientSnapshot[] { reconnected, b });
        Field<ServerDashboardPresenter>(form, "_presenter").ObserveClients([reconnected, b]);
        Select(reconnected.ClientId);
        if (scanView.SubnetInput.Text != "172.31.98.0/23") throw new Exception("Late network snapshot did not prefill subnet");
        Select(b.ClientId);
        if (scanView.SubnetInput.Text != "") throw new Exception("Previous client subnet leaked into B");
        Console.WriteLine("PASS Phase 11 UI: late network snapshot prefills /23; B never inherits A subnet");
        var telemetry = Field<TelemetryView>(form, "_dashboardTelemetry");
        telemetry.UpdateClient(a with { LastTelemetryAt = now.AddSeconds(-11) }, []);
        if (!telemetry.Heading.Text.Contains("STALE")) throw new Exception("Stale data presented as live");
        Console.WriteLine("PASS Phase 11 UI: stale telemetry is visibly labeled");

        void Select(string id)
        {
            SetField(form, "_selectedId", id);
            typeof(ServerDashboardForm).GetMethod("RenderSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        }
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
