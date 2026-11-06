using NetworkAdmin.Client.Application.Interfaces;
namespace NetworkAdmin.Client.UI.Services;

internal sealed class ScreenShareUi : IScreenShareConsent
{
    private Form? _owner;
    private Form? _statusForm;
    private bool _closingStatusProgrammatically;
    private Label? _controlStatus;
    private Button? _stopControlButton;
    private Action? _stopControlAction;

    public void Bind(Form owner) => _owner = owner;

    public Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(() =>
        {
            if (cancellationToken.IsCancellationRequested || _owner is null || _owner.IsDisposed)
            {
                completion.TrySetResult(false);
                return;
            }

            using var dialog = new Form
            {
                Text = "Screen sharing request",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                ClientSize = new Size(440, 160)
            };
            var label = new Label
            {
                Text = $"Server '{serverName}' requests to view this computer's screen.\n\nAllow screen sharing?",
                Dock = DockStyle.Top,
                Height = 95,
                TextAlign = ContentAlignment.MiddleCenter
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var accept = new Button { Text = "Accept", DialogResult = DialogResult.Yes, Width = 90 };
            var decline = new Button { Text = "Decline", DialogResult = DialogResult.No, Width = 90 };
            buttons.Controls.Add(accept);
            buttons.Controls.Add(decline);
            dialog.Controls.Add(buttons);
            dialog.Controls.Add(label);
            dialog.AcceptButton = accept;
            dialog.CancelButton = decline;
            using var timeout = new System.Windows.Forms.Timer { Interval = 60_000 };
            timeout.Tick += (_, _) => { timeout.Stop(); dialog.DialogResult = DialogResult.Cancel; };
            dialog.Shown += (_, _) => timeout.Start();
            using var registration = cancellationToken.Register(() =>
            {
                if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                try { dialog.BeginInvoke(new Action(() => dialog.DialogResult = DialogResult.Cancel)); }
                catch (InvalidOperationException) { }
            });
            var result = dialog.ShowDialog(_owner);
            completion.TrySetResult(result == DialogResult.Yes && !cancellationToken.IsCancellationRequested);
        }))
            completion.TrySetResult(false);
        return completion.Task.WaitAsync(cancellationToken);
    }

    public void SetSharing(bool active, Action stop)
    {
        Post(() =>
        {
            if (!active)
            {
                _closingStatusProgrammatically = true;
                try
                {
                    _statusForm?.Close();
                    _statusForm?.Dispose();
                }
                finally { _closingStatusProgrammatically = false; }
                _statusForm = null;
                _controlStatus = null;
                _stopControlButton = null;
                return;
            }

            if (_owner is null || _owner.IsDisposed || _statusForm is not null) return;
            var form = new Form
            {
                Text = "Screen sharing active",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(360, 155),
                ShowInTaskbar = true,
                TopMost = true
            };
            var label = new Label
            {
                Text = "Your screen is being shared with the Server.",
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.MiddleCenter
            };
            var button = new Button { Text = "Stop sharing", Dock = DockStyle.Bottom, Height = 38 };
            button.Click += (_, _) => stop();
            _controlStatus = new Label { Text = "Remote control: off", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
            _stopControlButton = new Button { Text = "Stop control", Dock = DockStyle.Bottom, Height = 32, Enabled = false };
            _stopControlButton.Click += (_, _) => _stopControlAction?.Invoke();
            form.Controls.Add(button);
            form.Controls.Add(_stopControlButton);
            form.Controls.Add(_controlStatus);
            form.Controls.Add(label);
            form.FormClosing += (_, _) =>
            {
                if (!_closingStatusProgrammatically) stop();
            };
            _statusForm = form;
            form.Show(_owner);
        });
    }

    public void SetControlState(bool active, Action stop)
    {
        Post(() =>
        {
            if (_controlStatus is null || _controlStatus.IsDisposed) return;
            _controlStatus.Text = active ? "Remote control: ACTIVE" : "Remote control: off";
            _stopControlAction = stop;
            if (_stopControlButton is not null) _stopControlButton.Enabled = active;
        });
    }

    public Task<bool> RequestControlConsentAsync(string serverName, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(() =>
        {
            if (cancellationToken.IsCancellationRequested || _owner is null || _owner.IsDisposed)
            { completion.TrySetResult(false); return; }
            using var dialog = new Form
            {
                Text = "Remote control request", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                ShowInTaskbar = false, ClientSize = new Size(470, 190)
            };
            var label = new Label
            {
                Text = $"Server '{serverName}' requests remote mouse and keyboard control over the TLS-protected connection. Allow control?",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(18)
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var accept = new Button { Text = "Accept", DialogResult = DialogResult.Yes, Width = 90 };
            var reject = new Button { Text = "Reject", DialogResult = DialogResult.No, Width = 90 };
            buttons.Controls.Add(accept); buttons.Controls.Add(reject);
            dialog.Controls.Add(label); dialog.Controls.Add(buttons);
            dialog.AcceptButton = accept; dialog.CancelButton = reject;
            using var registration = cancellationToken.Register(() =>
            {
                if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                try { dialog.BeginInvoke(new Action(() => dialog.DialogResult = DialogResult.Cancel)); }
                catch (InvalidOperationException) { }
            });
            completion.TrySetResult(dialog.ShowDialog(_owner) == DialogResult.Yes && !cancellationToken.IsCancellationRequested);
        }))
            completion.TrySetResult(false);
        return completion.Task.WaitAsync(cancellationToken);
    }

    private bool Post(Action action)
    {
        var owner = _owner;
        if (owner is null || owner.IsDisposed || !owner.IsHandleCreated) return false;
        try
        {
            owner.BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
