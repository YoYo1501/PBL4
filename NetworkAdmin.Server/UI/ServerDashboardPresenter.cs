using NetworkAdmin.Shared.Messages;
using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Server.UI;

// Owns dashboard state and command workflows; the form remains responsible for controls and rendering.
internal sealed class ServerDashboardPresenter
{
    private readonly ServerRuntime _runtime;
    private readonly TelemetryHistory _history = new();
    private readonly SessionCommandState _emptyCommands = new();

    public Dictionary<string, SessionCommandState> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ServerDashboardPresenter(ServerRuntime runtime) => _runtime = runtime;

    public SessionCommandState CommandsFor(string? clientId)
    {
        if (clientId is null) return _emptyCommands;
        if (!Commands.TryGetValue(clientId, out var state)) Commands[clientId] = state = new();
        return state;
    }

    public void ObserveClients(IReadOnlyList<ClientSnapshot> clients)
    {
        _history.Observe(clients);
        foreach (var client in clients)
        {
            var state = CommandsFor(client.ClientId);
            if (state.SessionId is not null && state.SessionId != client.SessionId)
            {
                if (state.PingPending) state.PingStatus = "Previous session Ping interrupted after reconnect.";
                if (state.ScanPending) state.ScanStatus = "Previous session scan interrupted after reconnect.";
                if (state.PowerPending) state.PowerStatus = "Previous session power command interrupted after reconnect.";
                if (state.LockPending) state.LockStatus = "Previous session lock command interrupted after reconnect.";
                state.PingPending = false;
                state.ScanPending = false;
                state.PowerPending = false;
                state.LockPending = false;
            }
            state.SessionId = client.SessionId;
        }

        var clientIds = clients.Select(client => client.ClientId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var clientId in Commands.Keys.Where(id => !clientIds.Contains(id)).ToArray())
            Commands.Remove(clientId);
    }

    public IReadOnlyList<TelemetryPoint> TelemetryFor(string? clientId) => _history.For(clientId);

    public async Task PingAsync(ClientSnapshot client, string target, CancellationToken token,
        Func<bool> isClosing, Action render, Action refresh)
    {
        var state = CommandsFor(client.ClientId);
        if (!client.Connected || state.PingPending) return;

        var sessionId = client.SessionId;
        var origin = $"Agent: {client.Hostname} ({client.IpAddress}) / Session {sessionId[..Math.Min(8, sessionId.Length)]}";
        state.PingPending = true;
        state.PingError = false;
        state.PingStatus = $"Pinging {target}...\n{origin}";
        render();
        try
        {
            var result = await _runtime.PingAsync(sessionId, target, token);
            if (isClosing() || !IsCurrentSession(client.ClientId, sessionId, state)) return;
            state.Ping = result;
            state.PingError = !result.Success;
            state.PingStatus = $"{result.StatusMessage} | Target: {result.Host} | {DateTime.Now:T}\n{origin}";
        }
        catch (Exception ex)
        {
            if (!isClosing() && IsCurrentSession(client.ClientId, sessionId, state))
            {
                state.PingStatus = $"Ping failed: {ex.Message}\n{origin}";
                state.PingError = true;
            }
        }
        finally
        {
            if (state.SessionId == sessionId) state.PingPending = false;
            if (!isClosing()) refresh();
        }
    }

    public async Task ScanAsync(ClientSnapshot client, string subnet, int timeoutMs, CancellationToken token,
        Func<bool> isClosing, Action render, Action refresh)
    {
        var state = CommandsFor(client.ClientId);
        if (!client.Connected || state.ScanPending) return;

        var sessionId = client.SessionId;
        var origin = $"Agent: {client.Hostname} / Session {sessionId[..Math.Min(8, sessionId.Length)]} | Subnet: {subnet}";
        state.ScanPending = true;
        state.ScanError = false;
        state.ScanStatus = $"Scanning...\n{origin}";
        render();
        try
        {
            var result = await _runtime.ScanAsync(sessionId, subnet, token, timeoutMs);
            if (isClosing() || !IsCurrentSession(client.ClientId, sessionId, state)) return;
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
            state.Scan = result;
            state.ScanAt = DateTime.Now;
            state.ScanOrigin = origin;
        }
        catch (Exception ex)
        {
            if (!isClosing() && IsCurrentSession(client.ClientId, sessionId, state))
            {
                state.ScanError = true;
                state.ScanStatus = $"Scan failed: {ex.Message}\n{origin}";
            }
        }
        finally
        {
            if (state.SessionId == sessionId) state.ScanPending = false;
            if (!isClosing()) refresh();
        }
    }

    public async Task GetAgentInfoAsync(ClientSnapshot client, CancellationToken token,
        Func<bool> isClosing, Action render, Action refresh)
    {
        var state = CommandsFor(client.ClientId);
        if (!client.Connected || state.RemoteInfoPending) return;

        var sessionId = client.SessionId;
        state.RemoteInfoPending = true;
        state.RemoteInfoError = false;
        state.RemoteInfoStatus = $"Requesting information from {client.Hostname}...";
        render();
        try
        {
            var result = await _runtime.GetAgentInfoAsync(client.ClientId, token);
            if (isClosing() || !IsCurrentSession(client.ClientId, sessionId, state)) return;
            state.RemoteInfoError = result.Status is not RemoteAdministrationCommandStatus.Completed;
            state.RemoteInfoStatus = result.Status == RemoteAdministrationCommandStatus.Completed
                ? result.Result
                : $"{result.Status}: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            if (!isClosing() && IsCurrentSession(client.ClientId, sessionId, state))
            {
                state.RemoteInfoError = true;
                state.RemoteInfoStatus = $"Remote command failed: {ex.Message}";
            }
        }
        finally
        {
            if (state.SessionId == sessionId) state.RemoteInfoPending = false;
            if (!isClosing()) refresh();
        }
    }

    public async Task PowerAsync(ClientSnapshot client, RemoteAdministrationCommandType command, int countdown,
        CancellationToken token, Func<bool> isClosing, Action render, Action refresh,
        Action<string, string, SessionCommandState, RemoteAdministrationResultMessage> progress)
    {
        var state = CommandsFor(client.ClientId);
        if (!client.Connected || state.PowerPending) return;

        var sessionId = client.SessionId;
        var action = command == RemoteAdministrationCommandType.Restart ? "restart" : "shut down";
        state.PowerPending = true;
        state.PowerError = false;
        state.PowerStatus = $"Sending {action} command to {client.Hostname}...";
        render();
        try
        {
            var result = await _runtime.PowerClientAsync(client.ClientId, command, countdown, token,
                update => progress(client.ClientId, sessionId, state, update));
            if (isClosing() || !IsCurrentSession(client.ClientId, sessionId, state)) return;
            state.PowerError = result.Status != RemoteAdministrationCommandStatus.Completed;
            state.PowerStatus = FormatPowerResult(action, result);
        }
        catch (Exception ex)
        {
            if (!isClosing() && IsCurrentSession(client.ClientId, sessionId, state))
            {
                state.PowerError = true;
                state.PowerStatus = $"{action} command failed: {ex.Message}";
            }
        }
        finally
        {
            if (state.SessionId == sessionId) state.PowerPending = false;
            if (!isClosing()) refresh();
        }
    }

    public bool IsCurrentSession(string clientId, string sessionId, SessionCommandState state) =>
        state.SessionId == sessionId && _runtime.GetClients().Any(client =>
            string.Equals(client.ClientId, clientId, StringComparison.OrdinalIgnoreCase)
            && client.SessionId == sessionId && client.Connected);

    public static string FormatPowerResult(string action, RemoteAdministrationResultMessage result) =>
        result.Status == RemoteAdministrationCommandStatus.Completed
            ? result.Result
            : $"{action} command {result.Status}: {result.ErrorMessage}";
}
