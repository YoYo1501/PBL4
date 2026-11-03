using NetworkAdmin.Server.UI;

namespace NetworkAdmin.Server.UI.Forms;

internal sealed class RemoteViewerForm : Form
{
    private readonly RemoteViewerPresenter _presenter;
    private readonly string _clientName;
    private readonly string _clientId;
    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, AutoScroll = false, BackColor = DashboardTheme.Background };
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(1, 10, 18) };
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
        _presenter = new RemoteViewerPresenter(connect);
        Text = $"Remote Viewer — {clientName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(1180, 760);
        BackColor = DashboardTheme.Background;
        ForeColor = DashboardTheme.Text;
        Font = new Font("Segoe UI", 9F);
        KeyPreview = true;
        BuildLayout();
        _presenter.StateChanged += OnStateChanged;
        _presenter.FrameReceived += OnFrameReceived;
        _toolTip.SetToolTip(_resolution, "Decoded JPEG frame resolution; the Client may downscale its virtual desktop before encoding.");
        _fitButton.Click += (_, _) => SetFitMode();
        _actualButton.Click += (_, _) => SetActualSizeMode();
        _fullScreenButton.Click += (_, _) => ToggleFullScreen();
        _stopButton.Click += (_, _) => Close();
        Shown += (_, _) => _presenter.Start();
        FormClosing += OnFormClosingAsync;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _fullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        };
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
        RemoteViewerFrame? frame;
        lock (_frameSync)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
            _frameUpdatePosted = false;
        }
        if (frame is null) return;
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
    }

    private void SetFitMode()
    {
        _actualSize = false;
        _canvas.AutoScroll = false;
        _picture.Dock = DockStyle.Fill;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
    }

    private void SetActualSizeMode()
    {
        _actualSize = true;
        _canvas.AutoScroll = true;
        _picture.Dock = DockStyle.None;
        _picture.SizeMode = PictureBoxSizeMode.Normal;
        if (_currentBitmap is not null) ApplyPictureSize(_currentBitmap.Width, _currentBitmap.Height);
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
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _fullScreenButton.Text = "Exit full screen";
            _fullScreen = true;
            return;
        }
        FormBorderStyle = _previousBorderStyle;
        WindowState = _previousWindowState == FormWindowState.Minimized ? FormWindowState.Normal : _previousWindowState;
        _fullScreenButton.Text = "Full screen";
        _fullScreen = false;
    }

    private void SetFooter(string text)
    {
        _detail.Text = text;
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
