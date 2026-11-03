namespace NetworkAdmin.Shared.Messages;

public static class RemoteInputEventPolicy
{
    public const int MaxCoordinate = 10_000;
    public const int MaxWheelDelta = 1_200;

    public static bool IsValid(RemoteInputEventMessage? input, string clientId, string sessionId,
        string screenRequestId, string controlRequestId)
    {
        if (input is null || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(screenRequestId) || string.IsNullOrWhiteSpace(controlRequestId) ||
            input.ClientId != clientId || input.SessionId != sessionId || input.ScreenRequestId != screenRequestId ||
            input.ControlRequestId != controlRequestId)
            return false;

        return input.Kind switch
        {
            RemoteInputEventKind.MouseMove => ValidCoordinates(input) && input.Button == 0 &&
                input.WheelDelta == 0 && input.VirtualKeyCode == 0,
            RemoteInputEventKind.MouseClick => ValidCoordinates(input) &&
                input.Button is RemoteMouseButton.Left or RemoteMouseButton.Right or RemoteMouseButton.Middle &&
                input.WheelDelta == 0 && input.VirtualKeyCode == 0,
            RemoteInputEventKind.MouseWheel => ValidCoordinates(input) && input.Button == 0 &&
                input.WheelDelta is >= -MaxWheelDelta and <= MaxWheelDelta && input.WheelDelta != 0 &&
                input.VirtualKeyCode == 0,
            RemoteInputEventKind.KeyDown or RemoteInputEventKind.KeyUp => input.X == 0 && input.Y == 0 &&
                input.Button == 0 && input.WheelDelta == 0 && input.VirtualKeyCode is >= 1 and <= 255,
            _ => false
        };
    }

    private static bool ValidCoordinates(RemoteInputEventMessage input) =>
        input.X is >= 0 and <= MaxCoordinate && input.Y is >= 0 and <= MaxCoordinate;
}
