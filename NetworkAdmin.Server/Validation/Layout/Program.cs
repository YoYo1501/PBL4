using System.Net;
using System.Reflection;
using NetworkAdmin.Server;
using NetworkAdmin.Server.UI;

internal static class LayoutValidation
{
    [STAThread]
    private static void Main(string[] args)
    {
        var baseline = args.Contains("--96");
        Application.SetHighDpiMode(baseline ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
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
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
