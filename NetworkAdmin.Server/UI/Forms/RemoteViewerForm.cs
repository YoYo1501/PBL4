using NetworkAdmin.Server.UI;
using System.Diagnostics;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.UI.Forms;

internal sealed class RemoteViewerForm : Form
{
    private readonly RemoteViewerPresenter _presenter;
    private readonly string _clientName;
    private readonly string _clientId;
    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, AutoScroll = false, BackColor = DashboardTheme.Background };
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(1, 10, 18), TabStop = true };
    private readonly Label _state = DashboardTheme.Label("WAITING", 9, true, DashboardTheme.Muted);
    private readonly Label _fps = DashboardTheme.Label("-- FPS", 9, true, DashboardTheme.Blue);
    private readonly Label _resolution = DashboardTheme.Label("-- × --", 9, color: DashboardTheme.Muted);
    private readonly Label _detail = DashboardTheme.Label("Waiting for Client to send the first frame…", 8, color: DashboardTheme.Muted);
    private readonly Button _fitButton = CreateToolButton("Fit to window", true);
    private readonly Button _actualButton = CreateToolButton("Actual size", false);
    private readonly Button _fullScreenButton = CreateToolButton("Full screen", false);
    private readonly Button _stopButton = CreateToolButton("Stop viewing", false, DashboardTheme.Red);
    private readonly Button _requestControlButton = CreateToolButton("Request Control", true);
    private readonly Button _stopControlButton = CreateToolButton("Stop Control", false, DashboardTheme.Red);
    private readonly Button _chatButton = CreateToolButton("Chat", false);
    private readonly Label _controlState = DashboardTheme.Label("CONTROL OFF", 9, true, DashboardTheme.Muted);
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _controlStateTimer = new() { Interval = 500 };
    private readonly object _frameSync = new();
    private Bitmap? _currentBitmap;
    private RemoteViewerFrame? _pendingFrame;
    private bool _frameUpdatePosted;
    private bool _actualSize;
    private bool _fullScreen;
    private bool _controlRequestPending;
    private RemoteMouseButton? _pressedMouseButton;
    private int _lastMappedX;
    private int _lastMappedY;
    private long _mouseMoveSentAt;
    private bool _allowClose;
    private int _closing;
    private FormBorderStyle _previousBorderStyle;
    private FormWindowState _previousWindowState;
    private RemoteChatForm? _chatForm;

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
        _requestControlButton.Click += async (_, _) => await RequestControlAsync();
        _stopControlButton.Click += async (_, _) => await StopControlAsync();
        _chatButton.Click += (_, _) => OpenChat();
        _picture.MouseEnter += (_, _) => _picture.Focus();
        _picture.MouseDown += (_, e) => OnPictureMouseDown(e);
        _picture.MouseUp += (_, e) => OnPictureMouseUp(e);
        _picture.MouseMove += (_, e) => OnPictureMouseMove(e);
        _picture.MouseWheel += (_, e) => OnPictureMouseWheel(e);
        _stopControlButton.Enabled = false;
        _requestControlButton.Enabled = false;
        _controlStateTimer.Tick += (_, _) => SyncControlState();
        _controlStateTimer.Start();
        Shown += (_, _) => _presenter.Start();
        FormClosing += OnFormClosingAsync;
        KeyDown += (_, e) => OnViewerKey(e, keyDown: true);
        KeyUp += (_, e) => OnViewerKey(e, keyDown: false);
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
        metrics.ColumnCount = 4;
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        metrics.Controls.Add(_controlState, 3, 0);

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        tools.Controls.AddRange([_fitButton, _actualButton, _fullScreenButton, _requestControlButton, _stopControlButton, _chatButton, _stopButton]);

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
            _requestControlButton.Enabled = state == RemoteViewerState.Streaming && !_presenter.IsControlActive && !_controlRequestPending;
            _chatButton.Enabled = state == RemoteViewerState.Streaming && _presenter.Chat is not null;
            if (state is RemoteViewerState.Disconnected or RemoteViewerState.Error)
            {
                _stopControlButton.Enabled = false;
                _controlState.Text = "CONTROL OFF";
                _controlState.ForeColor = DashboardTheme.Muted;
            }
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
        _picture.Dock = DockStyle.None;
        // Fit the complete remote frame without distorting its aspect ratio.
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        ApplyFitBounds();
        UpdateViewModeButtons();
    }

    private void ApplyFitBounds()
    {
        if (_canvas.ClientSize.Width <= 0 || _canvas.ClientSize.Height <= 0) return;
        _picture.SetBounds(0, 0, _canvas.ClientSize.Width, _canvas.ClientSize.Height);
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

    private void OnPictureMouseDown(MouseEventArgs e)
    {
        _picture.Focus();
        if (!_presenter.IsControlActive || !TryMapToNormalized(e.Location, out var x, out var y)) return;
        var button = e.Button switch
        {
            MouseButtons.Left => RemoteMouseButton.Left,
            MouseButtons.Right => RemoteMouseButton.Right,
            MouseButtons.Middle => RemoteMouseButton.Middle,
            _ => (RemoteMouseButton)0
        };
        if (button != 0)
        {
            _pressedMouseButton = button;
            _lastMappedX = x;
            _lastMappedY = y;
            QueueInput(RemoteInputEventKind.MouseDown, x, y, button, 0, 0);
        }
    }

    private void OnPictureMouseUp(MouseEventArgs e)
    {
        if (_pressedMouseButton is not { } button) return;
        if (!_presenter.IsControlActive)
        {
            _pressedMouseButton = null;
            return;
        }
        if (TryMapToNormalized(e.Location, out var x, out var y))
        {
            _lastMappedX = x;
            _lastMappedY = y;
        }
        else
        {
            x = _lastMappedX;
            y = _lastMappedY;
        }
        _pressedMouseButton = null;
        QueueInput(RemoteInputEventKind.MouseUp, x, y, button, 0, 0);
    }

    private void OnPictureMouseMove(MouseEventArgs e)
    {
        if (!_presenter.IsControlActive || !TryMapToNormalized(e.Location, out var x, out var y)) return;
        var now = Stopwatch.GetTimestamp();
        if (_mouseMoveSentAt != 0 && Stopwatch.GetElapsedTime(_mouseMoveSentAt, now) < TimeSpan.FromMilliseconds(16)) return;
        _mouseMoveSentAt = now;
        _lastMappedX = x;
        _lastMappedY = y;
        QueueInput(RemoteInputEventKind.MouseMove, x, y, 0, 0, 0);
    }

    private void OnPictureMouseWheel(MouseEventArgs e)
    {
        if (_presenter.IsControlActive && TryMapToNormalized(e.Location, out var x, out var y) && e.Delta != 0)
            QueueInput(RemoteInputEventKind.MouseWheel, x, y, 0, Math.Clamp(e.Delta, -1200, 1200), 0);
    }

    private void OnViewerKey(KeyEventArgs e, bool keyDown)
    {
        if (keyDown && e.KeyCode == Keys.Escape && _fullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (!_presenter.IsControlActive || !_picture.Focused || e.KeyValue is < 1 or > 255) return;
        QueueInput(keyDown ? RemoteInputEventKind.KeyDown : RemoteInputEventKind.KeyUp,
            0, 0, 0, 0, e.KeyValue);
        e.Handled = true;
        if (keyDown) e.SuppressKeyPress = true;
    }

    private bool TryMapToNormalized(Point point, out int x, out int y)
    {
        x = y = 0;
        var image = _currentBitmap;
        return image is not null && TryMapToNormalized(point, _picture.ClientSize, image.Size, _picture.SizeMode, out x, out y);
    }

    internal static bool TryMapToNormalized(Point point, Size viewport, Size image, PictureBoxSizeMode mode,
        out int x, out int y)
    {
        x = y = 0;
        if (viewport.Width <= 0 || viewport.Height <= 0) return false;
        var displayed = CalculateDisplayedImageBounds(viewport, image, mode);
        var imageX = point.X - displayed.X;
        var imageY = point.Y - displayed.Y;
        if (displayed.IsEmpty || imageX < 0 || imageY < 0 || imageX >= displayed.Width || imageY >= displayed.Height) return false;
        x = Math.Clamp((int)Math.Round(imageX / displayed.Width * RemoteInputEventPolicy.MaxCoordinate), 0, RemoteInputEventPolicy.MaxCoordinate);
        y = Math.Clamp((int)Math.Round(imageY / displayed.Height * RemoteInputEventPolicy.MaxCoordinate), 0, RemoteInputEventPolicy.MaxCoordinate);
        return true;
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

    private void QueueInput(RemoteInputEventKind kind, int x, int y, RemoteMouseButton button, int wheelDelta, int virtualKeyCode) =>
        _ = SendInputEventAsync(kind, x, y, button, wheelDelta, virtualKeyCode);

    private async Task SendInputEventAsync(RemoteInputEventKind kind, int x, int y,
        RemoteMouseButton button, int wheelDelta, int virtualKeyCode)
    {
        try { await _presenter.SendInputEventAsync(kind, x, y, button, wheelDelta, virtualKeyCode); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetFooter($"Remote input was not sent: {ex.GetBaseException().Message}"); }
    }

    private async Task RequestControlAsync()
    {
        if (!_requestControlButton.Enabled) return;
        _controlRequestPending = true;
        _requestControlButton.Enabled = false;
        _controlState.Text = "WAITING FOR CLIENT";
        _controlState.ForeColor = DashboardTheme.Blue;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(65));
            var accepted = await _presenter.RequestControlAsync(timeout.Token);
            _controlState.Text = accepted ? "ACTIVE" : "CONTROL REJECTED";
            _controlState.ForeColor = accepted ? DashboardTheme.Green : DashboardTheme.Muted;
            _stopControlButton.Enabled = accepted;
            _requestControlButton.Enabled = !accepted && _presenter.State == RemoteViewerState.Streaming;
            SetFooter(accepted
                ? "Client approved control. Use the viewer to send mouse and keyboard input; Stop Control revokes access."
                : "Client rejected the control request.");
        }
        catch (Exception ex)
        {
            _controlState.Text = "CONTROL UNAVAILABLE";
            _controlState.ForeColor = DashboardTheme.Red;
            _requestControlButton.Enabled = _presenter.State == RemoteViewerState.Streaming;
            SetFooter($"Control request failed: {ex.GetBaseException().Message}");
        }
        finally
        {
            _controlRequestPending = false;
            if (!_presenter.IsControlActive && _presenter.State == RemoteViewerState.Streaming)
                _requestControlButton.Enabled = true;
        }
    }

    private void OpenChat()
    {
        var chat = _presenter.Chat;
        if (chat is null || _chatForm is { IsDisposed: false })
        {
            _chatForm?.BringToFront();
            return;
        }

        var form = new RemoteChatForm(_clientName, chat.SendAsync);
        Action<RemoteChatMessage> onMessage = form.AppendMessage;
        chat.MessageReceived += onMessage;
        form.FormClosed += (_, _) =>
        {
            chat.MessageReceived -= onMessage;
            if (ReferenceEquals(_chatForm, form)) _chatForm = null;
        };
        _chatForm = form;
        form.Show(this);
    }

    private async Task StopControlAsync()
    {
        _stopControlButton.Enabled = false;
        try { await _presenter.StopControlAsync(); }
        catch (Exception ex) { SetFooter($"Stop control failed: {ex.GetBaseException().Message}"); }
        _pressedMouseButton = null;
        _controlState.Text = "CONTROL OFF";
        _controlState.ForeColor = DashboardTheme.Muted;
        _requestControlButton.Enabled = _presenter.State == RemoteViewerState.Streaming;
        SetFooter("Remote control stopped. Screen viewing continues.");
    }

    private void SyncControlState()
    {
        if (IsDisposed) return;
        var active = _presenter.IsControlActive;
        if (!active && _controlState.Text.StartsWith("ACTIVE", StringComparison.Ordinal))
        {
            _pressedMouseButton = null;
            _controlState.Text = "CONTROL OFF";
            _controlState.ForeColor = DashboardTheme.Muted;
            _stopControlButton.Enabled = false;
            _requestControlButton.Enabled = true;
            SetFooter("Client stopped remote control. Screen viewing continues.");
        }
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
        _chatForm?.Close();
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
            _controlStateTimer.Dispose();
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
