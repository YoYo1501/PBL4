using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using NetworkAdmin.Shared.Protocol;
using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Interfaces;
using NetworkAdmin.Shared.Models;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientCommandHandler
{
    private readonly IPingService _pingService;
    private readonly ServerConnection _connection;
    private readonly string _adminToken;
    private readonly SemaphoreSlim _pingSlots = new(4, 4);
    private readonly ConcurrentDictionary<Task, byte> _runningTasks = new();
    private readonly ConcurrentDictionary<string, byte> _remoteCommandIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _remoteCancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanSync = new();
    private INetworkScannerService? _scannerService;
    private Task _scanTask = Task.CompletedTask;

    // The connection's single read loop calls HandleAsync. Drain after that loop stops.
    public async Task DrainAsync()
    {
        while (true)
        {
            RemoveCompletedTasks();
            var tasks = _runningTasks.Keys.ToArray();
            if (tasks.Length == 0)
                return;

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Background command cleanup: {ex.Message}");
            }
        }
    }

    public ClientCommandHandler(IPingService pingService, ServerConnection connection, string adminToken = "")
    {
        _pingService = pingService;
        _connection = connection;
        _adminToken = adminToken;
    }

    public void SetScanner(INetworkScannerService scannerService)
    {
        _scannerService = scannerService;
    }

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Type == MessageType.PingRequest)
        {
            TrackTask(Task.Run(() => HandlePingCommandAsync(envelope, cancellationToken), CancellationToken.None));
            return;
        }

        if (envelope.Type == MessageType.ScanRequest)
        {
            var scanStarted = false;
            lock (_scanSync)
            {
                if (_scanTask.IsCompleted)
                {
                    _scanTask = Task.Run(() => HandleScanCommandAsync(envelope, cancellationToken), CancellationToken.None);
                    TrackTask(_scanTask);
                    scanStarted = true;
                }
            }

            if (!scanStarted)
            {
                await TrySendAsync(MessageType.ScanResult, envelope.RequestId,
                    new ScanResultMessage { Success = false, ErrorMessage = "A scan is already running on this client." }, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (envelope.Type == MessageType.RemoteAdministrationCommand)
        {
            TrackTask(Task.Run(() => HandleRemoteAdministrationCommandAsync(envelope, cancellationToken), CancellationToken.None));
            return;
        }

        if (envelope.Type == MessageType.RemoteAdministrationCancel)
        {
            HandleRemoteAdministrationCancel(envelope);
            return;
        }

        Console.WriteLine($"Ignoring unsupported server message: {envelope.Type}");
    }

    private async Task HandleRemoteAdministrationCommandAsync(
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        RemoteAdministrationCommandMessage? request = null;
        CancellationTokenSource? commandCancellation = null;
        try
        {
            if (string.IsNullOrWhiteSpace(envelope.RequestId))
            {
                await SendRemoteResultAsync(envelope.RequestId, null,
                    RemoteAdministrationCommandStatus.Rejected, "RequestId is required.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!_remoteCommandIds.TryAdd(envelope.RequestId, 0))
            {
                await SendRemoteResultAsync(envelope.RequestId, null,
                    RemoteAdministrationCommandStatus.Rejected, "Duplicate remote command request.", cancellationToken).ConfigureAwait(false);
                return;
            }

            request = envelope.Data.Deserialize<RemoteAdministrationCommandMessage>(JsonOptions);
            if (request is null || !IsAuthorized(request.AuthorizationToken))
            {
                await SendRemoteResultAsync(envelope.RequestId, request?.Command,
                    RemoteAdministrationCommandStatus.Rejected, "Remote administration authorization failed.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (request.Command != RemoteAdministrationCommandType.GetAgentInfo)
            {
                await SendRemoteResultAsync(envelope.RequestId, request.Command,
                    RemoteAdministrationCommandStatus.Rejected, "Remote command is not permitted on this client.", cancellationToken).ConfigureAwait(false);
                return;
            }

            commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_remoteCancellations.TryAdd(envelope.RequestId, commandCancellation))
            {
                await SendRemoteResultAsync(envelope.RequestId, request.Command,
                    RemoteAdministrationCommandStatus.Rejected, "Remote command is already running.", cancellationToken).ConfigureAwait(false);
                return;
            }

            await SendRemoteResultAsync(envelope.RequestId, request.Command,
                RemoteAdministrationCommandStatus.Accepted, string.Empty, commandCancellation.Token).ConfigureAwait(false);

            var result = $"ClientId={_connection.ClientId}; ClientName={_connection.ClientName}; " +
                         $"SessionId={_connection.SessionId}; MachineName={Environment.MachineName}; " +
                         $"OS={Environment.OSVersion}";
            await SendRemoteResultAsync(envelope.RequestId, request.Command,
                RemoteAdministrationCommandStatus.Completed, result, commandCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            await SendRemoteResultAsync(envelope.RequestId, request?.Command,
                RemoteAdministrationCommandStatus.Failed, $"Command canceled: {ex.Message}", cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await SendRemoteResultAsync(envelope.RequestId, request?.Command,
                RemoteAdministrationCommandStatus.Rejected, $"Malformed remote command: {ex.Message}", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Remote command stopped: {ex.Message}");
            await SendRemoteResultAsync(envelope.RequestId, request?.Command,
                RemoteAdministrationCommandStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_remoteCancellations.TryRemove(envelope.RequestId, out var cancellation))
                cancellation.Dispose();
        }
    }

    private void HandleRemoteAdministrationCancel(MessageEnvelope envelope)
    {
        try
        {
            var request = envelope.Data.Deserialize<RemoteAdministrationCancelMessage>(JsonOptions);
            if (request is not null && IsAuthorized(request.AuthorizationToken)
                && _remoteCancellations.TryGetValue(envelope.RequestId, out var cancellation))
            {
                cancellation.Cancel();
                Console.WriteLine($"Remote command canceled: {envelope.RequestId} ({request.Reason})");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Malformed remote cancellation: {ex.Message}");
        }
    }

    private async Task SendRemoteResultAsync(
        string requestId,
        RemoteAdministrationCommandType? command,
        RemoteAdministrationCommandStatus status,
        string message,
        CancellationToken cancellationToken)
    {
        await TrySendAsync(MessageType.RemoteAdministrationResult, requestId,
            new RemoteAdministrationResultMessage
            {
                Command = command ?? default,
                Status = status,
                ClientId = _connection.ClientId,
                Result = status == RemoteAdministrationCommandStatus.Completed ? message : string.Empty,
                ErrorMessage = status == RemoteAdministrationCommandStatus.Completed ? string.Empty : message,
                TimestampUtc = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);
    }

    private bool IsAuthorized(string token)
    {
        if (string.IsNullOrEmpty(_adminToken) || string.IsNullOrEmpty(token))
            return false;

        var expected = System.Text.Encoding.UTF8.GetBytes(_adminToken);
        var actual = System.Text.Encoding.UTF8.GetBytes(token);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task HandlePingCommandAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var slotAcquired = false;
        try
        {
            if (!_pingSlots.Wait(0))
            {
                await TrySendAsync(MessageType.PingResult, envelope.RequestId, new PingResult
                {
                    Success = false,
                    RoundtripTimeMs = 0,
                    StatusMessage = "Ping rejected: maximum 4 concurrent ping requests are already running."
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            slotAcquired = true;
            var result = await HandlePingAsync(envelope, cancellationToken).ConfigureAwait(false);
            await TrySendAsync(MessageType.PingResult, envelope.RequestId, result, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"Ping task canceled: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ping task stopped: {ex.Message}");
        }
        finally
        {
            if (slotAcquired)
                _pingSlots.Release();
        }
    }

    private async Task<PingResult> HandlePingAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var request = envelope.Data.Deserialize<PingRequestMessage>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var target = request?.Target?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(target))
            {
                return new PingResult
                {
                    Host = target,
                    Success = false,
                    RoundtripTimeMs = 0,
                    StatusMessage = "Invalid ping target"
                };
            }

            Console.WriteLine($"PingRequest received: {target}");
            var result = await _pingService.PingSeriesAsync(target, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"PingResult sent: {target} -> {result.StatusMessage}");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException ex)
        {
            return new PingResult
            {
                Host = string.Empty,
                Success = false,
                RoundtripTimeMs = 0,
                StatusMessage = $"Malformed PingRequest: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new PingResult
            {
                Host = string.Empty,
                Success = false,
                RoundtripTimeMs = 0,
                StatusMessage = $"Ping failed: {ex.Message}"
            };
        }
    }

    private async Task HandleScanCommandAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var result = await HandleScanAsync(envelope, cancellationToken).ConfigureAwait(false);
            await TrySendAsync(MessageType.ScanResult, envelope.RequestId, result, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) { Console.WriteLine($"Scan task canceled: {ex.Message}"); }
        catch (Exception ex) { Console.WriteLine($"Scan task stopped: {ex.Message}"); }
    }

    private async Task TrySendAsync<TPayload>(MessageType type, string requestId, TPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            await _connection.SendAsync(type, requestId, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"{type} delivery canceled: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{type} delivery stopped: {ex.Message}");
        }
    }

    private void TrackTask(Task task)
    {
        _runningTasks.TryAdd(task, 0);
        _ = task.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsFaulted && completedTask.Exception is not null)
                    Console.WriteLine($"Background command faulted: {completedTask.Exception.GetBaseException().Message}");

                _runningTasks.TryRemove(completedTask, out _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RemoveCompletedTasks()
    {
        foreach (var task in _runningTasks.Keys)
        {
            if (task.IsCompleted)
                _runningTasks.TryRemove(task, out _);
        }
    }

    private async Task<ScanResultMessage> HandleScanAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            if (_scannerService is null)
            {
                return new ScanResultMessage
                {
                    Success = false,
                    ErrorMessage = "Network scanner is not available."
                };
            }

            var request = envelope.Data.Deserialize<ScanRequestMessage>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new ScanRequestMessage();

            var subnet = request.Subnet?.Trim() ?? string.Empty;
            var timeoutMs = ScanSubnet.ClampTimeout(request.TimeoutMs);

            if (!string.IsNullOrWhiteSpace(subnet) && !ScanSubnet.TryNormalize(subnet, out subnet))
            {
                return new ScanResultMessage
                {
                    Success = false,
                    Subnet = subnet,
                    ErrorMessage = ScanSubnet.Help
                };
            }

            Console.WriteLine(string.IsNullOrWhiteSpace(subnet)
                ? "ScanRequest received: local network"
                : $"ScanRequest received: {subnet}");

            var devices = string.IsNullOrWhiteSpace(subnet)
                ? await Task.Run(() => _scannerService.ScanLocalNetwork(cancellationToken: cancellationToken), cancellationToken)
                    .ConfigureAwait(false)
                : await _scannerService.ScanNetworkAsync(subnet, timeoutMs, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            Console.WriteLine($"ScanResult sent: devices={devices.Count}");
            return new ScanResultMessage
            {
                Success = true,
                Subnet = subnet,
                Devices = devices
            };
        }
        catch (JsonException ex)
        {
            return new ScanResultMessage
            {
                Success = false,
                ErrorMessage = $"Malformed ScanRequest: {ex.Message}"
            };
        }
        catch (OperationCanceledException)
        {
            return new ScanResultMessage
            {
                Success = false,
                ErrorMessage = "Scan canceled."
            };
        }
        catch (Exception ex)
        {
            return new ScanResultMessage
            {
                Success = false,
                ErrorMessage = $"Scan failed: {ex.Message}"
            };
        }
    }

}
