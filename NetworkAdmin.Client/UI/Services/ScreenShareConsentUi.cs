using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.UI.Forms;

namespace NetworkAdmin.Client.UI.Services;

/// <summary>WinForms consent prompt and local stop control for view-only screen sharing.</summary>
internal sealed class ScreenShareConsentUi : IScreenShareConsent
{
    private readonly object _sync = new();
    private Form? _statusForm;
    private bool _closingStatus;

    public Task<bool> RequestConsentAsync(string serverName, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(owner =>
        {
            if (cancellationToken.IsCancellationRequested || owner.IsDisposed)
            {
                completion.TrySetResult(false);
                return;
            }
            using var dialog = new Form
            {
                Text = "Screen viewing request",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                ClientSize = new Size(450, 165)
            };
            var description = new Label
            {
                Text = $"Server '{serverName}' asks to view this computer's screen.\n\nAllow screen sharing?",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(14)
            };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var allow = new Button { Text = "Allow", DialogResult = DialogResult.Yes, Width = 90 };
            var decline = new Button { Text = "Decline", DialogResult = DialogResult.No, Width = 90 };
            buttons.Controls.Add(allow);
            buttons.Controls.Add(decline);
            dialog.Controls.Add(description);
            dialog.Controls.Add(buttons);
            dialog.AcceptButton = allow;
            dialog.CancelButton = decline;
            using var registration = cancellationToken.Register(() =>
            {
                if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
                try { dialog.BeginInvoke((Action)(() => dialog.DialogResult = DialogResult.Cancel)); }
                catch (InvalidOperationException) { }
            });
            var result = dialog.ShowDialog(owner);
            completion.TrySetResult(result == DialogResult.Yes && !cancellationToken.IsCancellationRequested);
        }))
            completion.TrySetResult(false);
        return completion.Task.WaitAsync(cancellationToken);
    }

    public void SetSharing(bool active, Action stop)
    {
        Post(owner =>
        {
            if (!active)
            {
                lock (_sync)
                {
                    _closingStatus = true;
                    try { _statusForm?.Close(); }
                    finally { _statusForm = null; _closingStatus = false; }
                }
                return;
            }
            lock (_sync)
            {
                if (_statusForm is { IsDisposed: false } || owner.IsDisposed) return;
                var form = new Form
                {
                    Text = "Screen sharing active",
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedToolWindow,
                    ClientSize = new Size(360, 125),
                    ShowInTaskbar = true,
                    TopMost = true
                };
                var label = new Label
                {
                    Text = "Your screen is being viewed by the Server.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = new Padding(12)
                };
                var stopButton = new Button { Text = "Stop sharing", Dock = DockStyle.Bottom, Height = 38 };
                stopButton.Click += (_, _) => stop();
                form.Controls.Add(label);
                form.Controls.Add(stopButton);
                form.FormClosing += (_, e) =>
                {
                    if (_closingStatus) return;
                    e.Cancel = true;
                    stop();
                };
                _statusForm = form;
                form.Show(owner);
            }
        });
    }

    private static bool Post(Action<Form> action)
    {
        var forms = System.Windows.Forms.Application.OpenForms.Cast<Form>().Where(f => !f.IsDisposed && f.IsHandleCreated).ToArray();
        var owner = forms.FirstOrDefault(f => f is MainForm) ?? forms.FirstOrDefault();
        if (owner is null) return false;
        try
        {
            owner.BeginInvoke((Action)(() => action(owner)));
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }
}
