using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.UI.Forms;

internal sealed class ClientChatForm : Form
{
    private readonly IClientChatSession _session;
    private readonly RichTextBox _history = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, ForeColor = Color.Black };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill, MaxLength = NetworkAdmin.Shared.Protocol.RemoteProtocolContract.MaxChatTextLength };
    private readonly Button _send = new() { Text = "Send", AutoSize = true, Dock = DockStyle.Right };
    private int _sending;

    public ClientChatForm(IClientChatSession session)
    {
        _session = session;
        Text = "NetworkAdmin Chat";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 360);
        MinimumSize = new Size(360, 260);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(4) };
        bottom.Controls.Add(_input);
        bottom.Controls.Add(_send);
        Controls.Add(_history);
        Controls.Add(bottom);
        _send.Click += async (_, _) => await SendCurrentAsync();
        _input.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Shift) return;
            e.SuppressKeyPress = true;
            await SendCurrentAsync();
        };
    }

    public void AppendMessage(RemoteChatMessage message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => AppendMessage(message))); } catch (InvalidOperationException) { }
            return;
        }
        var sender = message.SenderRole == RemoteChatSenderRole.Admin ? "Administrator" : "You";
        _history.AppendText($"[{message.TimestampUtc.ToLocalTime():HH:mm:ss}] {sender}: {message.Content}{Environment.NewLine}");
        _history.SelectionStart = _history.TextLength;
        _history.ScrollToCaret();
    }

    private async Task SendCurrentAsync()
    {
        if (Interlocked.Exchange(ref _sending, 1) != 0) return;
        try
        {
            var content = _input.Text.Trim();
            if (string.IsNullOrWhiteSpace(content)) return;
            _send.Enabled = false;
            await _session.SendAsync(content);
            _input.Clear();
            AppendMessage(new RemoteChatMessage { SenderRole = RemoteChatSenderRole.Client, Content = content, TimestampUtc = DateTime.UtcNow });
        }
        catch (Exception ex) { AppendMessage(new RemoteChatMessage { SenderRole = RemoteChatSenderRole.System, Content = $"Send failed: {ex.GetBaseException().Message}", TimestampUtc = DateTime.UtcNow }); }
        finally { _send.Enabled = true; Interlocked.Exchange(ref _sending, 0); }
    }
}
