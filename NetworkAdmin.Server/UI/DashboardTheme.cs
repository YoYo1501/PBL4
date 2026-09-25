using System.Drawing.Drawing2D;

namespace NetworkAdmin.Server.UI;

internal static class DashboardTheme
{
    public static readonly Color Background = Color.FromArgb(3, 19, 35);
    public static readonly Color Sidebar = Color.FromArgb(3, 23, 41);
    public static readonly Color Surface = Color.FromArgb(6, 31, 51);
    public static readonly Color Input = Color.FromArgb(8, 37, 60);
    public static readonly Color Border = Color.FromArgb(20, 65, 91);
    public static readonly Color Text = Color.FromArgb(233, 242, 255);
    public static readonly Color Muted = Color.FromArgb(151, 180, 207);
    public static readonly Color Blue = Color.FromArgb(43, 166, 248);
    public static readonly Color Green = Color.FromArgb(25, 221, 146);
    public static readonly Color Purple = Color.FromArgb(167, 123, 251);
    public static readonly Color Red = Color.FromArgb(250, 105, 143);

    public static Label Label(string text = "", float size = 10, bool bold = false, Color? color = null) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = color ?? Text, BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0)
    };

    public static TableLayoutPanel Table(int columns, int rows)
    {
        var table = new BufferedTable
        {
            Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        // A default AutoSize row preserves the pre-scale preferred height and overflows
        // its parent. Single-row horizontal layouts must fill their allocated height.
        if (rows == 1) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    public static TableLayoutPanel Rows(params float[] heights)
    {
        var table = Table(1, heights.Length);
        table.RowStyles.Clear();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var h in heights) table.RowStyles.Add(new RowStyle(h < 0 ? SizeType.Percent : SizeType.Absolute, Math.Abs(h)));
        return table;
    }

    public static Button Button(string text, bool primary = true) => new()
    {
        Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        BackColor = primary ? Color.FromArgb(8, 79, 179) : Input, ForeColor = Text,
        FlatAppearance = { BorderColor = Border, MouseOverBackColor = Color.FromArgb(15, 91, 154) },
        Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(4), MinimumSize = new Size(0, 32)
    };

    public static TextBox InputBox(string placeholder) => new()
    {
        Dock = DockStyle.Fill, BackColor = Input, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 11), PlaceholderText = placeholder, Margin = new Padding(4, 8, 8, 4)
    };

    public static DataGridView Grid(params (string Name, string Title, int Weight)[] columns)
    {
        var grid = new BufferedGrid
        {
            Dock = DockStyle.Fill, BackgroundColor = Surface, BorderStyle = BorderStyle.None,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false,
            GridColor = Border, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeight = 30, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Text,
                SelectionBackColor = Color.FromArgb(7, 65, 113), SelectionForeColor = Text, Padding = new Padding(5, 0, 3, 0) },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(5, 27, 46) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Input, ForeColor = Text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(5, 0, 3, 0) },
            Font = new Font("Segoe UI", 9), Margin = new Padding(0)
        };
        grid.RowTemplate.Height = 27;
        foreach (var (name, title, weight) in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, DataPropertyName = name,
                HeaderText = title, FillWeight = weight, MinimumWidth = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
        return grid;
    }

    public static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "--" : value;
    public static string Percent(float? value) => value is null ? "--" : $"{value:0.0}%";
    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class BufferedTable : TableLayoutPanel
{
    public BufferedTable() => DoubleBuffered = true;
}
internal sealed class BufferedGrid : DataGridView
{
    public BufferedGrid() => DoubleBuffered = true;
}

internal class DashboardCard : Panel
{
    public Panel Body { get; } = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    public Label Heading { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = DashboardTheme.Border;
    public DashboardCard(string title)
    {
        DoubleBuffered = true; Dock = DockStyle.Fill; Padding = new Padding(14, 8, 14, 12);
        Margin = new Padding(6); BackColor = DashboardTheme.Surface;
        Heading = DashboardTheme.Label(title, 10, true, Color.FromArgb(186, 223, 249));
        Heading.Dock = DockStyle.Top; Heading.Height = 32;
        Controls.Add(Body); Controls.Add(Heading);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = DashboardTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using var pen = new Pen(Accent);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class MetricCard : DashboardCard
{
    private readonly Label _value = DashboardTheme.Label("--", 18, true);
    private readonly Label _caption = DashboardTheme.Label("", 9, color: DashboardTheme.Muted);
    public MetricCard(string title, Color accent) : base(title)
    {
        Accent = accent; Heading.ForeColor = DashboardTheme.Text;
        BackColor = Color.FromArgb(5 + accent.R / 8, 25 + accent.G / 10, 44 + accent.B / 8);
        Heading.Visible = false;
        _value.ForeColor = DashboardTheme.Text;
        var columns = DashboardTheme.Table(2, 1);
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        columns.Controls.Add(new MetricSymbol(title, accent), 0, 0);
        var rows = DashboardTheme.Rows(-30, -42, -28);
        rows.Padding = new Padding(10, 0, 0, 0);
        rows.Controls.Add(DashboardTheme.Label(title, 10, true), 0, 0);
        rows.Controls.Add(_value, 0, 1); rows.Controls.Add(_caption, 0, 2);
        columns.Controls.Add(rows, 1, 0); Body.Controls.Add(columns);
    }
    public void UpdateValue(string value, string caption) { _value.Text = value; _caption.Text = caption; }
}

internal sealed class MetricSymbol : Control
{
    private readonly string _kind;
    private readonly Color _accent;
    public MetricSymbol(string kind, Color accent)
    {
        _kind = kind; _accent = accent; Dock = DockStyle.Fill; DoubleBuffered = true;
        BackColor = Color.FromArgb(6 + accent.R / 5, 28 + accent.G / 6, 45 + accent.B / 5);
        Margin = new Padding(0, 8, 0, 8);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height);
        g.TranslateTransform((Width - size) / 2f, (Height - size) / 2f);
        g.ScaleTransform(size / 64f, size / 64f);
        using var pen = new Pen(_accent, 2.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (_kind.StartsWith("Managed"))
        {
            g.DrawRectangle(pen, 12, 15, 40, 28); g.DrawLine(pen, 32, 44, 32, 51); g.DrawLine(pen, 23, 52, 41, 52);
        }
        else if (_kind.StartsWith("Online"))
        {
            g.DrawPolygon(pen, [new(32, 10), new(49, 17), new(46, 40), new(32, 53), new(18, 40), new(15, 17)]);
            g.DrawLines(pen, [new(23, 30), new(30, 37), new(41, 24)]);
        }
        else if (_kind.StartsWith("Hosts"))
        {
            g.DrawEllipse(pen, 26, 10, 12, 12); g.DrawLine(pen, 32, 23, 32, 33);
            g.DrawLine(pen, 15, 33, 49, 33); g.DrawLine(pen, 15, 33, 15, 41); g.DrawLine(pen, 49, 33, 49, 41);
            g.DrawRectangle(pen, 8, 42, 14, 10); g.DrawRectangle(pen, 42, 42, 14, 10);
        }
        else g.DrawLines(pen, [new(8, 32), new(20, 32), new(26, 14), new(36, 50), new(43, 28), new(49, 32), new(57, 32)]);
    }
}

internal sealed class BrandMark : Control
{
    public BrandMark() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = DashboardTheme.Sidebar; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width - 20, Height - 8);
        var r = new Rectangle((Width - size) / 2, 4, size, size);
        using var pen = new Pen(DashboardTheme.Blue, 2);
        e.Graphics.DrawEllipse(pen, r);
        e.Graphics.DrawEllipse(pen, r.X + size / 3, r.Y, size / 3, size);
        e.Graphics.DrawEllipse(pen, r.X, r.Y + size / 3, size, size / 3);
    }
}
