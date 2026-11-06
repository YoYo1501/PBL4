using NetworkAdmin.Server.UI;
using System.Diagnostics;
using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Diagnostics;
using NetworkAdmin.Shared.Protocol;

namespace NetworkAdmin.Server.UI.Forms;

internal sealed class RemoteViewerForm : Form
{
    private readonly RemoteViewerPresenter _presenter;
    private readonly ScreenSharePerformanceMonitor? _performance;
    private readonly string _clientName;
    private readonly string _clientId;
    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, AutoScroll = false, BackColor = DashboardTheme.Background };
    private TableLayoutPanel? _rootLayout;
    private Panel? _headerPanel;
    private readonly MeasuredPictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(1, 10, 18), TabStop = true };
    private readonly Label _state = DashboardTheme.Label("WAITING", 9, true, DashboardTheme.Muted);
    private readonly Label _fps = DashboardTheme.Label("-- FPS", 9, true, DashboardTheme.Blue);
    private readonly Label _resolution = DashboardTheme.Label("-- × --", 9, color: DashboardTheme.Muted);
    private readonly Label _detail = DashboardTheme.Label("Waiting for Client to send the first frame…", 8, color: DashboardTheme.Muted);
    private readonly Button _fitButton = CreateToolButton("Fit to window", true);
    private readonly Button _actualButton = CreateToolButton("Actual size", false);
    private readonly Button _fullScreenButton = CreateToolButton("Full screen", false);
    private readonly Button _stopButton = CreateToolButton("Stop viewing", false, DashboardTheme.Red);
    private readonly ToolTip _toolTip = new();
    private readonly object _frameSync = new();
    private Bitmap? _currentBitmap;
    private RemoteViewerFrame? _pendingFrame;
    private bool _frameUpdatePosted;
    private bool _actualSize;
    private bool _fullScreen;
    private bool _allowClose;
    private int _closing;
    private FormBorderStyle _previousBorderStyle;
    private FormWindowState _previousWindowState;

    public RemoteViewerForm(string clientName, string clientId, IScreenViewerSession session)
        : this(clientName, clientId, (_, _) => Task.FromResult(session)) { }

    public RemoteViewerForm(string clientName, string clientId,
        Func<CancellationToken, Action, Task<IScreenViewerSession>> connect)
    {
        _clientName = clientName;
        _clientId = clientId;
        _performance = ScreenSharePerformanceMonitor.Enabled ? new ScreenSharePerformanceMonitor() : null;
        _presenter = new RemoteViewerPresenter(connect, _performance, ScreenShareV2Configuration.IsEnabled);
        _picture.Performance = _performance;
        Text = $"Remote Viewer — {clientName}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(1180, 760);
        WindowState = FormWindowState.Maximized;
        BackColor = DashboardTheme.Background;
        ForeColor = DashboardTheme.Text;
        Font = new Font("Segoe UI", 9F);
        KeyPreview = true;
        BuildLayout();
        _canvas.Resize += (_, _) =>
        {
            if (!_actualSize) ApplyFitBounds();
        };
        _presenter.StateChanged += OnStateChanged;
        _presenter.FrameReceived += OnFrameReceived;
        _toolTip.SetToolTip(_resolution, "Decoded frame resolution received from the Client.");
        _fitButton.Click += (_, _) => SetFitMode();
        _actualButton.Click += (_, _) => SetActualSizeMode();
        // Fit mode is the safe default: show the complete remote desktop inside
        // the viewer even when the server monitor has a different resolution.
        SetFitMode();
        _fullScreenButton.Click += (_, _) => ToggleFullScreen();
        _stopButton.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _fullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        };
        Shown += (_, _) => _presenter.Start();
        FormClosing += OnFormClosingAsync;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardTheme.Background,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        _rootLayout = root;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardTheme.Surface,
            Padding = new Padding(12, 4, 8, 4),
            Margin = new Padding(0, 0, 0, 8)
        };
        _headerPanel = header;
        var headerGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var client = DashboardTheme.Label(_clientName, 12, true);
        client.Margin = new Padding(2, 0, 4, 0);
        var identity = DashboardTheme.Label($"Client ID  {_clientId}", 8, color: DashboardTheme.Muted);
        identity.Margin = new Padding(2, 0, 4, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(2, 0, 2, 0)
        };
        _state.Width = TextRenderer.MeasureText("DISCONNECTED", _state.Font).Width + 12;
        _fps.Width = TextRenderer.MeasureText("20.0 FPS", _fps.Font).Width + 12;
        _resolution.Width = TextRenderer.MeasureText("2560 × 1440", _resolution.Font).Width + 12;
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _state.Width));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _fps.Width));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _resolution.Width));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        metrics.Controls.Add(_state, 0, 0);
        metrics.Controls.Add(_fps, 1, 0);
        metrics.Controls.Add(_resolution, 2, 0);

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        tools.Controls.AddRange([_fitButton, _actualButton, _fullScreenButton, _stopButton]);

        headerGrid.Controls.Add(client, 0, 0);
        headerGrid.Controls.Add(tools, 1, 0);
        headerGrid.Controls.Add(identity, 0, 1);
        headerGrid.Controls.Add(metrics, 1, 1);
        header.Controls.Add(headerGrid);
        _canvas.Controls.Add(_picture);
        _detail.Name = "viewerStatusDetail";
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_canvas, 0, 1);
        root.Controls.Add(_detail, 0, 2);
        Controls.Add(root);
    }

    private void OnStateChanged(RemoteViewerState state, string? detail)
    {
        PostToUi(() =>
        {
            _state.Text = state switch
            {
                RemoteViewerState.Waiting => "WAITING",
                RemoteViewerState.Connecting => "CONNECTING",
                RemoteViewerState.Streaming => "STREAMING",
                RemoteViewerState.Disconnected => "DISCONNECTED",
                _ => "ERROR"
            };
            _state.ForeColor = state switch
            {
                RemoteViewerState.Streaming => DashboardTheme.Green,
                RemoteViewerState.Error => DashboardTheme.Red,
                RemoteViewerState.Disconnected => DashboardTheme.Muted,
                _ => DashboardTheme.Blue
            };
            if (!string.IsNullOrWhiteSpace(detail)) SetFooter(detail);
            else if (state == RemoteViewerState.Connecting) SetFooter("Screen channel connected; waiting for frames…");
            else if (state == RemoteViewerState.Disconnected) SetFooter("Screen sharing ended. The Client remains connected.");
        });
    }

    private void OnFrameReceived(RemoteViewerFrame frame)
    {
        lock (_frameSync)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                frame.Image.Dispose();
                return;
            }
            if (_pendingFrame is not null) _performance?.DroppedFrame();
            _pendingFrame?.Image.Dispose();
            _pendingFrame = frame;
            if (_frameUpdatePosted) return;
            _frameUpdatePosted = true;
        }
        try
        {
            BeginInvoke(new Action(ApplyPendingFrame));
        }
        catch (InvalidOperationException)
        {
            lock (_frameSync)
            {
                _pendingFrame?.Image.Dispose();
                _pendingFrame = null;
                _frameUpdatePosted = false;
            }
        }
    }

    private void ApplyPendingFrame()
    {
        var renderStarted = Stopwatch.GetTimestamp();
        RemoteViewerFrame? frame;
        lock (_frameSync)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
            _frameUpdatePosted = false;
        }
        if (frame is null) return;
        _performance?.ViewerFrame();
        _performance?.ViewerLatency(Stopwatch.GetTimestamp() - frame.ReceivedAtTimestamp);
        if (IsDisposed)
        {
            frame.Image.Dispose();
            return;
        }
        var old = _currentBitmap;
        _currentBitmap = frame.Image;
        _picture.Image = frame.Image;
        _fps.Text = $"{frame.FramesPerSecond:0.0} FPS";
        _resolution.Text = $"{frame.Width} × {frame.Height}";
        SetFooter($"Streaming from {_clientName}  •  frame {frame.Sequence}");
        if (_actualSize) ApplyPictureSize(frame.Width, frame.Height);
        old?.Dispose();
        _performance?.UiUpdate(Stopwatch.GetTimestamp() - renderStarted);
    }

    private void SetFitMode()
    {
        _actualSize = false;
        _canvas.AutoScroll = false;
        // Let WinForms keep the image viewport equal to the canvas on every
        // resize/DPI change. Zoom then fits the complete remote frame instead
        // of leaving a 3200x2000 bitmap at its native size in a smaller window.
        _picture.Dock = DockStyle.Fill;
        // Fit the complete remote frame without distorting its aspect ratio.
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        ApplyFitBounds();
        UpdateViewModeButtons();
    }

    private void ApplyFitBounds()
    {
        if (_canvas.ClientSize.Width <= 0 || _canvas.ClientSize.Height <= 0) return;
        _picture.Dock = DockStyle.Fill;
        _picture.BringToFront();
    }

    private void SetActualSizeMode()
    {
        _actualSize = true;
        _canvas.AutoScroll = true;
        _picture.Dock = DockStyle.None;
        _picture.SizeMode = PictureBoxSizeMode.Normal;
        if (_currentBitmap is not null) ApplyPictureSize(_currentBitmap.Width, _currentBitmap.Height);
        UpdateViewModeButtons();
    }

    private void UpdateViewModeButtons()
    {
        // The button colors indicate which scaling mode is currently active.
        var active = Color.FromArgb(8, 79, 179);
        var inactive = DashboardTheme.Input;
        _fitButton.BackColor = _actualSize ? inactive : active;
        _actualButton.BackColor = _actualSize ? active : inactive;
        _fitButton.AccessibleDescription = _actualSize ? "Switch to fit the complete screen in the viewer" : "Active: complete screen fitted in the viewer";
        _actualButton.AccessibleDescription = _actualSize ? "Active: show the remote screen at its captured size" : "Switch to actual captured size";
    }

    private void ApplyPictureSize(int width, int height)
    {
        _picture.Location = Point.Empty;
        _picture.Size = new Size(width, height);
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _previousBorderStyle = FormBorderStyle;
            _previousWindowState = WindowState;
            _rootLayout!.RowStyles[0].Height = 0;
            _headerPanel!.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _rootLayout.PerformLayout();
            _fullScreenButton.Text = "Exit full screen";
            _fullScreen = true;
            return;
        }
        FormBorderStyle = _previousBorderStyle;
        WindowState = _previousWindowState == FormWindowState.Minimized ? FormWindowState.Normal : _previousWindowState;
        _rootLayout!.RowStyles[0].Height = 116;
        _headerPanel!.Visible = true;
        _rootLayout.PerformLayout();
        _fullScreenButton.Text = "Full screen";
        _fullScreen = false;
    }

    private void SetFooter(string text)
    {
        _detail.Text = text;
    }

    internal static RectangleF CalculateDisplayedImageBounds(Size viewport, Size image, PictureBoxSizeMode mode)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || image.Width <= 0 || image.Height <= 0)
            return RectangleF.Empty;
        if (mode == PictureBoxSizeMode.Zoom)
        {
            var scale = Math.Min((double)viewport.Width / image.Width, (double)viewport.Height / image.Height);
            var width = (float)(image.Width * scale);
            var height = (float)(image.Height * scale);
            return new RectangleF((viewport.Width - width) / 2f, (viewport.Height - height) / 2f, width, height);
        }
        if (mode == PictureBoxSizeMode.StretchImage)
            return new RectangleF(0, 0, viewport.Width, viewport.Height);
        return new RectangleF(0, 0, image.Width, image.Height);
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private async void OnFormClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _stopButton.Enabled = false;
        _stopButton.Text = "Stopping…";
        try { await _presenter.DisposeAsync(); }
        catch (Exception ex) { SetFooter($"Cleanup: {ex.Message}"); }
        _presenter.StateChanged -= OnStateChanged;
        _presenter.FrameReceived -= OnFrameReceived;
        _allowClose = true;
        if (!IsDisposed) Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_frameSync)
            {
                _pendingFrame?.Image.Dispose();
                _pendingFrame = null;
            }
            _currentBitmap?.Dispose();
            _currentBitmap = null;
            _toolTip.Dispose();
            _performance?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Button CreateToolButton(string text, bool primary, Color? accent = null)
    {
        var font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        return new Button
        {
            Text = text,
            Width = TextRenderer.MeasureText(text, font).Width + 22,
            Height = Math.Max(34, font.Height + 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ?? (primary ? Color.FromArgb(8, 79, 179) : DashboardTheme.Input),
            ForeColor = DashboardTheme.Text,
            Font = font,
            Cursor = Cursors.Hand,
            Margin = new Padding(3),
            FlatAppearance = { BorderColor = DashboardTheme.Border, MouseOverBackColor = Color.FromArgb(15, 91, 154) }
        };
    }
}

internal sealed class MeasuredPictureBox : PictureBox
{
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ScreenSharePerformanceMonitor? Performance { get; set; }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var started = Stopwatch.GetTimestamp();
        base.OnPaint(pe);
        Performance?.Render(Stopwatch.GetTimestamp() - started);
    }
}
