using System.Text.Json;
using NetworkAdmin.Client.Infrastructure.Security;
using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Client.Tests;

public sealed class SecurityEventCollectorTests
{
    [Fact]
    public async Task Ndjson_fixture_parses_events_and_handles_malformed_lines()
    {
        var directory = CreateTempDirectory();
        try
        {
            var file = Path.Combine(directory, "winlogbeat_output_1.ndjson");
            await File.WriteAllTextAsync(file, "not-json\n" + FixtureLine(1));
            var options = Options(directory);
            await using var collector = new SecurityEventCollector(options);
            var received = new TaskCompletionSource<SecurityEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            collector.EventReceived += message => received.TrySetResult(message);

            await collector.StartAsync();
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("client-a", message.ClientId);
            Assert.Equal("session-a", message.SessionId);
            Assert.Equal("1", message.EventId);
            Assert.Equal("Microsoft-Windows-Sysmon", message.Source);
            Assert.Equal("cmd.exe", message.ProcessName);
            Assert.Equal("DOMAIN\\user", message.UserName);
            Assert.Equal(443, message.DestinationPort);

            await collector.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(MonitoringState.Stopped, collector.State);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Ndjson_fixture_supports_partial_lines_rotation_and_checkpoint_offset()
    {
        var directory = CreateTempDirectory();
        try
        {
            var file = Path.Combine(directory, "winlogbeat_output_1.ndjson");
            var checkpoint = Path.Combine(directory, "checkpoint.json");
            var firstLine = FixtureLine(1);
            await File.WriteAllTextAsync(file, firstLine[..^1]);
            var options = Options(directory, checkpoint);
            await using var firstCollector = new SecurityEventCollector(options);
            var first = new TaskCompletionSource<SecurityEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            firstCollector.EventReceived += message => first.TrySetResult(message);
            await firstCollector.StartAsync();
            await Task.Delay(100);
            Assert.False(first.Task.IsCompleted);

            await File.AppendAllTextAsync(file, "\n");
            Assert.Equal("1", (await first.Task.WaitAsync(TimeSpan.FromSeconds(5))).EventId);
            await firstCollector.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(File.Exists(checkpoint));

            await File.AppendAllTextAsync(file, FixtureLine(2));
            await using var secondCollector = new SecurityEventCollector(options);
            var second = new TaskCompletionSource<SecurityEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            secondCollector.EventReceived += message => second.TrySetResult(message);
            await secondCollector.StartAsync();
            Assert.Equal("2", (await second.Task.WaitAsync(TimeSpan.FromSeconds(5))).EventId);

            var rotated = Path.Combine(directory, "winlogbeat_output_2.ndjson");
            await File.WriteAllTextAsync(rotated, FixtureLine(3));
            File.SetLastWriteTimeUtc(rotated, DateTime.UtcNow.AddMinutes(1));
            var third = new TaskCompletionSource<SecurityEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            secondCollector.EventReceived += message => third.TrySetResult(message);
            Assert.Equal("3", (await third.Task.WaitAsync(TimeSpan.FromSeconds(5))).EventId);
            await secondCollector.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_ndjson_source_reports_status_and_stops_on_cancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "missing-security-source-" + Guid.NewGuid().ToString("N"));
        var options = Options(directory);
        await using var collector = new SecurityEventCollector(options);
        var statuses = new List<MonitoringStatusMessage>();
        collector.StatusChanged += status => statuses.Add(status);

        await collector.StartAsync();
        await Task.Delay(100);
        await collector.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MonitoringState.Stopped, collector.State);
        Assert.Contains(statuses, status => status.Detail.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    private static SecurityEventCollectorOptions Options(string directory, string? checkpoint = null) => new()
    {
        ClientId = "client-a",
        SessionId = "session-a",
        SourceKind = SecurityEventSourceKind.Ndjson,
        NdjsonDirectory = directory,
        NdjsonPattern = "winlogbeat_output*.ndjson",
        CheckpointFile = checkpoint,
        PollInterval = TimeSpan.FromMilliseconds(20),
        QueueCapacity = 2,
        SourceQueueCapacity = 2
    };

    private static string FixtureLine(int eventCode)
    {
        var payload = new
        {
            @timestamp = "2026-11-05T00:00:00Z",
            @event = new { code = eventCode, provider = "Microsoft-Windows-Sysmon" },
            winlog = new
            {
                event_data = new
                {
                    Image = @"C:\Windows\System32\cmd.exe",
                    CommandLine = "cmd.exe /c whoami",
                    ParentImage = @"C:\Windows\explorer.exe",
                    User = "DOMAIN\\user"
                }
            },
            process = new { name = "cmd.exe" },
            destination = new { port = 443 }
        };
        return JsonSerializer.Serialize(payload) + "\n";
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "networkadmin-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
