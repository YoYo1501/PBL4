using System.Collections.Concurrent;
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
    private readonly SemaphoreSlim _pingSlots = new(4, 4);
    private readonly ConcurrentDictionary<Task, byte> _runningTasks = new();
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

    public ClientCommandHandler(IPingService pingService, ServerConnection connection)
    {
        _pingService = pingService;
        _connection = connection;
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

        Console.WriteLine($"Ignoring unsupported server message: {envelope.Type}");
    }

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
