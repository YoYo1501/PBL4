using NetworkAdmin.Client.Application.Interfaces;
namespace NetworkAdmin.Client.UI.Services;

internal sealed class ScreenShareUi : IScreenShareConsent
{
    private Form? _owner;
    private Form? _statusForm;
    private bool _closingStatusProgrammatically;

    public void Bind(Form owner) => _owner = owner;

    public Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
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
        });
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
                return;
            }

            if (_owner is null || _owner.IsDisposed || _statusForm is not null) return;
            var form = new Form
            {
                Text = "Screen sharing active",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(340, 110),
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
            form.Controls.Add(button);
            form.Controls.Add(label);
            form.FormClosing += (_, _) =>
            {
                if (!_closingStatusProgrammatically) stop();
            };
            _statusForm = form;
            form.Show(_owner);
        });
    }

    private void Post(Action action)
    {
        var owner = _owner;
        if (owner is null || owner.IsDisposed || !owner.IsHandleCreated) return;
        try { owner.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }
}
