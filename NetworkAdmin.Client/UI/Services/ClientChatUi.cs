using NetworkAdmin.Client.Application.Interfaces;
using NetworkAdmin.Client.UI.Forms;
using NetworkAdmin.Shared.Messages;

namespace NetworkAdmin.Client.UI.Services;

/// <summary>WinForms adapter for the independent Client chat session.</summary>
internal sealed class ClientChatUi : IClientChatUi
{
    private readonly object _sync = new();
    private IClientChatSession? _session;
    private Action<RemoteChatMessage>? _messageHandler;
    private ClientChatForm? _form;

    public void Attach(IClientChatSession session)
    {
        ClientChatForm? oldForm;
        lock (_sync)
        {
            if (_session is not null && _messageHandler is not null)
                _session.MessageReceived -= _messageHandler;
            oldForm = _form;
            _form = null;
            _session = session;
            _messageHandler = message => OnMessageReceived(session, message);
            _session.MessageReceived += _messageHandler;
        }
        CloseOnUiThread(oldForm);
    }

    public void Detach(IClientChatSession session)
    {
        ClientChatForm? form;
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            if (_messageHandler is not null) session.MessageReceived -= _messageHandler;
            _messageHandler = null;
            _session = null;
            form = _form;
            _form = null;
        }
        if (form is null || form.IsDisposed) return;
        CloseOnUiThread(form);
    }

    private static void CloseOnUiThread(ClientChatForm? form)
    {
        if (form is null || form.IsDisposed) return;
        try
        {
            if (form.IsHandleCreated && form.InvokeRequired) form.BeginInvoke((Action)form.Close);
            else form.Close();
        }
        catch (InvalidOperationException) { }
    }

    private void OnMessageReceived(IClientChatSession session, RemoteChatMessage message)
    {
        var owner = System.Windows.Forms.Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.IsHandleCreated && !form.IsDisposed);
        if (owner is null) return;
        void ShowMessage()
        {
            if (owner.IsDisposed) return;
            lock (_sync)
            {
                if (_form is null || _form.IsDisposed)
                {
                if (!ReferenceEquals(_session, session)) return;
                    _form = new ClientChatForm(session);
                }
                if (!_form.Visible) _form.Show(owner);
                _form.Activate();
                _form.AppendMessage(message);
            }
        }
        try
        {
            if (owner.InvokeRequired) owner.BeginInvoke((Action)ShowMessage);
            else ShowMessage();
        }
        catch (InvalidOperationException) { }
    }
}
