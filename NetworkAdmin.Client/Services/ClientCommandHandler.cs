using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using NetworkAdmin.Shared.Messages;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;

namespace NetworkAdmin.Client.Services;

internal sealed class ClientCommandHandler
{
    private readonly IPingService _pingService;
    private readonly ServerConnection _connection;
    private INetworkScannerService? _scannerService;

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
            var result = await HandlePingAsync(envelope, cancellationToken).ConfigureAwait(false);
            await _connection.SendAsync(MessageType.PingResult, envelope.RequestId, result, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (envelope.Type == MessageType.ScanRequest)
        {
            _ = Task.Run(() => HandleScanCommandAsync(envelope, cancellationToken), CancellationToken.None);
            return;
        }

        if (envelope.Type != MessageType.PingRequest)
        {
            Console.WriteLine($"Ignoring unsupported server message: {envelope.Type}");
            return;
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
            var result = await _pingService.PingAsync(target, timeoutMs: 1000, retryCount: 1).ConfigureAwait(false);
            Console.WriteLine($"PingResult sent: {target} -> {result.StatusMessage}");
            return result;
        }
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
        var result = await HandleScanAsync(envelope, cancellationToken).ConfigureAwait(false);
        await _connection.SendAsync(MessageType.ScanResult, envelope.RequestId, result, cancellationToken)
            .ConfigureAwait(false);
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
            var timeoutMs = request.TimeoutMs <= 0 ? 800 : request.TimeoutMs;

            if (!string.IsNullOrWhiteSpace(subnet) && !IsSupportedScanSubnet(subnet))
            {
                return new ScanResultMessage
                {
                    Success = false,
                    Subnet = subnet,
                    ErrorMessage = "Invalid scan subnet."
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

    private static bool IsSupportedScanSubnet(string subnet)
    {
        var legacyParts = subnet.Split('.', StringSplitOptions.TrimEntries);
        if (legacyParts.Length == 3)
        {
            return legacyParts.All(part => int.TryParse(part, out var octet) && octet >= 0 && octet <= 255);
        }

        var cidrParts = subnet.Split('/', StringSplitOptions.TrimEntries);
        if (cidrParts.Length != 2 ||
            !IPAddress.TryParse(cidrParts[0], out var baseAddress) ||
            baseAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return int.TryParse(cidrParts[1], out var prefixLength) && prefixLength is >= 1 and <= 30;
    }
}
