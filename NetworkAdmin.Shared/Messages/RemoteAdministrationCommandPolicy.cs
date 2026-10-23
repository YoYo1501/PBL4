namespace NetworkAdmin.Shared.Messages;

public static class RemoteAdministrationCommandPolicy
{
    public const int DefaultCountdownSeconds = 30;
    public const int MinimumCountdownSeconds = 5;
    public const int MaximumCountdownSeconds = 60;

    public static bool IsSupported(RemoteAdministrationCommandType command) =>
        command is RemoteAdministrationCommandType.GetAgentInfo
            or RemoteAdministrationCommandType.Shutdown
            or RemoteAdministrationCommandType.Restart;

    public static bool IsPowerCommand(RemoteAdministrationCommandType command) =>
        command is RemoteAdministrationCommandType.Shutdown or RemoteAdministrationCommandType.Restart;

    public static int ClampCountdown(int seconds) => Math.Clamp(
        seconds <= 0 ? DefaultCountdownSeconds : seconds,
        MinimumCountdownSeconds,
        MaximumCountdownSeconds);

    public static string? Validate(RemoteAdministrationCommandMessage? request)
    {
        if (request is null)
            return "Remote command payload is required.";
        if (!IsSupported(request.Command))
            return "Remote command is not supported.";
        if (IsPowerCommand(request.Command)
            && (request.CountdownSeconds < MinimumCountdownSeconds || request.CountdownSeconds > MaximumCountdownSeconds))
            return $"Countdown must be between {MinimumCountdownSeconds} and {MaximumCountdownSeconds} seconds.";
        return null;
    }
}
