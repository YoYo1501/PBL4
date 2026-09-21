namespace NetworkAdmin.Server;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var options = ServerOptions.Load(args);
        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var clientManager = new ClientManager();
        var dispatcher = new ServerCommandDispatcher(clientManager);
        var server = new TcpHelloServer(options, clientManager, dispatcher);
        var serverTask = server.RunAsync(cancellation.Token);
        var commandTask = RunCommandLoopAsync(clientManager, dispatcher, cancellation.Token);

        await Task.WhenAny(serverTask, commandTask).ConfigureAwait(false);
        cancellation.Cancel();
        await Task.WhenAll(serverTask, commandTask).ConfigureAwait(false);
    }

    private static async Task RunCommandLoopAsync(
        ClientManager clientManager,
        ServerCommandDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Commands: clients | ping <sessionId> <target> | scan <sessionId> [subnet] | exit");

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0].Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (parts[0].Equals("clients", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var session in clientManager.GetSessions())
                {
                    Console.WriteLine($"{session.SessionId} {session.ClientName} {session.ClientId}");
                }

                continue;
            }

            if (parts.Length >= 3 && parts[0].Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var result = await dispatcher.SendPingAsync(
                        parts[1],
                        parts[2],
                        TimeSpan.FromSeconds(5),
                        cancellationToken).ConfigureAwait(false);

                    Console.WriteLine($"Ping command completed: {result.Host} {result.StatusMessage}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ping command failed: {ex.Message}");
                }

                continue;
            }

            if (parts.Length >= 2 && parts[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var subnet = parts.Length >= 3 ? parts[2] : string.Empty;
                    var result = await dispatcher.SendScanAsync(
                        parts[1],
                        subnet,
                        TimeSpan.FromSeconds(60),
                        cancellationToken).ConfigureAwait(false);

                    Console.WriteLine(result.Success
                        ? $"Scan command completed: devices={result.Devices.Count}"
                        : $"Scan command completed with error: {result.ErrorMessage}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Scan command failed: {ex.Message}");
                }

                continue;
            }

            Console.WriteLine("Unknown command.");
        }
    }
}
