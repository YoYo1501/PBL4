using NetworkAdmin.Shared.Messages;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class RemoteAdministrationCommandPolicyTests
{
    [Theory]
    [InlineData(RemoteAdministrationCommandType.GetAgentInfo)]
    [InlineData(RemoteAdministrationCommandType.Shutdown)]
    [InlineData(RemoteAdministrationCommandType.Restart)]
    public void Supported_commands_are_accepted(RemoteAdministrationCommandType command)
    {
        var request = new RemoteAdministrationCommandMessage
        {
            Command = command,
            CountdownSeconds = RemoteAdministrationCommandPolicy.DefaultCountdownSeconds
        };

        Assert.Null(RemoteAdministrationCommandPolicy.Validate(request));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(61)]
    public void Power_commands_reject_invalid_countdown(int countdown)
    {
        var request = new RemoteAdministrationCommandMessage
        {
            Command = RemoteAdministrationCommandType.Shutdown,
            CountdownSeconds = countdown
        };

        Assert.Contains("Countdown", RemoteAdministrationCommandPolicy.Validate(request));
    }

    [Fact]
    public void Countdown_is_clamped_to_the_shared_policy_range()
    {
        Assert.Equal(RemoteAdministrationCommandPolicy.MinimumCountdownSeconds,
            RemoteAdministrationCommandPolicy.ClampCountdown(1));
        Assert.Equal(RemoteAdministrationCommandPolicy.MaximumCountdownSeconds,
            RemoteAdministrationCommandPolicy.ClampCountdown(999));
        Assert.Equal(RemoteAdministrationCommandPolicy.DefaultCountdownSeconds,
            RemoteAdministrationCommandPolicy.ClampCountdown(0));
    }

    [Fact]
    public void Lifecycle_keeps_acceptance_preparation_and_completion_distinct()
    {
        var lifecycle = new[]
        {
            RemoteAdministrationCommandStatus.Accepted,
            RemoteAdministrationCommandStatus.Preparing,
            RemoteAdministrationCommandStatus.Completed
        };

        Assert.Equal(RemoteAdministrationCommandStatus.Accepted, lifecycle[0]);
        Assert.Equal(RemoteAdministrationCommandStatus.Preparing, lifecycle[1]);
        Assert.Equal(RemoteAdministrationCommandStatus.Completed, lifecycle[2]);
        Assert.NotEqual(lifecycle[1], lifecycle[2]);
    }
}
