namespace NetworkAdmin.Shared.Protocol;

public static class ScreenShareV2Configuration
{
    public const string EnvironmentVariable = "NETWORKADMIN_SCREEN_V2";

    public static bool IsEnabled => Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static bool Parse(string? value) => bool.TryParse(value, out var enabled) && enabled;
}
