using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Server.UI.Forms;

internal sealed class RemoteChatForm : Form
{
    private readonly Func<string, CancellationToken, Task> _send;
    private readonly RichTextBox _history = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, ForeColor = Color.Black };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly Button _sendButton = new() { Text = "Send", AutoSize = true, Dock = DockStyle.Right };
    private int _sending;

    public RemoteChatForm(string clientName, Func<string, CancellationToken, Task> send)
    {
        _send = send;
        Text = $"Remote Chat — {clientName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 420);
        MinimumSize = new Size(420, 300);
        _sendButton.Click += async (_, _) => await SendCurrentAsync();
        _input.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Shift) return;
            e.SuppressKeyPress = true;
            await SendCurrentAsync();
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(4) };
        bottom.Controls.Add(_input);
        bottom.Controls.Add(_sendButton);
        Controls.Add(_history);
        Controls.Add(bottom);
    }

    public void AppendMessage(RemoteChatMessage message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => AppendMessage(message))); } catch (InvalidOperationException) { }
            return;
        }
        var sender = message.SenderRole == RemoteChatSenderRole.Admin ? "You" : message.SenderId;
        _history.AppendText($"[{message.TimestampUtc.ToLocalTime():HH:mm:ss}] {sender}: {message.Content}{Environment.NewLine}");
        _history.SelectionStart = _history.TextLength;
        _history.ScrollToCaret();
    }

    public void AppendSystem(string message)
    {
        if (IsDisposed) return;
        _history.AppendText($"[{DateTime.Now:HH:mm:ss}] System: {message}{Environment.NewLine}");
    }

    private async Task SendCurrentAsync()
    {
        if (Interlocked.Exchange(ref _sending, 1) != 0) return;
        try
        {
            var content = _input.Text.Trim();
            if (string.IsNullOrWhiteSpace(content)) return;
            _sendButton.Enabled = false;
            await _send(content, CancellationToken.None).ConfigureAwait(true);
            _input.Clear();
            AppendMessage(new RemoteChatMessage
            {
                SenderRole = RemoteChatSenderRole.Admin,
                SenderId = "admin",
                Content = content,
                TimestampUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex) { AppendSystem($"Send failed: {ex.GetBaseException().Message}"); }
        finally
        {
            _sendButton.Enabled = true;
            Interlocked.Exchange(ref _sending, 0);
        }
    }
}
