using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.UI.Services;

internal sealed class ClientChatUi : IClientChatUi, IDisposable
{
    private readonly object _sync = new();
    private Form? _owner;
    private Form? _window;
    private RichTextBox? _history;
    private TextBox? _input;
    private Func<string, CancellationToken, Task>? _send;
    private bool _connected;

    public void Bind(Form owner) => _owner = owner;

    public void BindTransport(Func<string, CancellationToken, Task> sendAsync)
    {
        lock (_sync) _send = sendAsync;
    }

    public void SetConnectionState(bool connected, string? detail = null)
    {
        _connected = connected;
        if (!connected && detail is not null) RunOnUi(() => AppendSystem(detail));
    }

    public void ShowMessage(RemoteChatMessage message)
    {
        RunOnUi(() =>
        {
            EnsureWindow();
            AppendLine(message.SenderRole == RemoteChatSenderRole.Admin ? "Admin" : message.SenderId,
                message.TimestampUtc.ToLocalTime(), message.Content);
            if (_window is not null && !_window.Visible) _window.Show(_owner);
            _window?.BringToFront();
        });
    }

    public void Dispose()
    {
        lock (_sync) _send = null;
        if (_window is not null && !_window.IsDisposed) _window.Dispose();
        _window = null;
    }

    private void EnsureWindow()
    {
        if (_window is not null && !_window.IsDisposed) return;
        _window = new Form
        {
            Text = "Remote Chat",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(520, 420),
            MinimumSize = new Size(420, 300)
        };
        _history = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, ForeColor = Color.Black };
        _input = new TextBox { Dock = DockStyle.Fill };
        var send = new Button { Text = "Send", AutoSize = true, Dock = DockStyle.Right };
        send.Click += async (_, _) => await SendCurrentAsync();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(4) };
        bottom.Controls.Add(_input);
        bottom.Controls.Add(send);
        _window.Controls.Add(_history);
        _window.Controls.Add(bottom);
        _window.FormClosing += (_, e) =>
        {
            e.Cancel = true;
            _window.Hide();
        };
        if (!_connected) AppendSystem("Chat is disconnected; messages are not queued.");
    }

    private async Task SendCurrentAsync()
    {
        var text = _input?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        Func<string, CancellationToken, Task>? send;
        lock (_sync) send = _send;
        if (!_connected || send is null)
        {
            AppendSystem("Chat is disconnected.");
            return;
        }
        try
        {
            await send(text, CancellationToken.None).ConfigureAwait(true);
            if (_input is not null) _input.Clear();
            AppendLine("You", DateTime.Now, text);
        }
        catch (Exception ex) { AppendSystem($"Send failed: {ex.GetBaseException().Message}"); }
    }

    private void AppendSystem(string text) => AppendLine("System", DateTime.Now, text);

    private void AppendLine(string sender, DateTime timestamp, string content)
    {
        if (_history is null) return;
        _history.AppendText($"[{timestamp:HH:mm:ss}] {sender}: {content}{Environment.NewLine}");
        _history.SelectionStart = _history.TextLength;
        _history.ScrollToCaret();
    }

    private void RunOnUi(Action action)
    {
        var owner = _owner;
        if (owner is null || owner.IsDisposed) return;
        if (owner.InvokeRequired)
        {
            try { owner.BeginInvoke(action); } catch (InvalidOperationException) { }
            return;
        }
        action();
    }
}
