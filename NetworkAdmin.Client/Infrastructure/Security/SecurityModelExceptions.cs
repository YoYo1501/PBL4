namespace NetworkAdmin.Client.Infrastructure.Security;

internal sealed class SecurityModelContractException : Exception
{
    public SecurityModelContractException(string message) : base(message) { }
}

internal sealed class SecurityInferenceTimeoutException : TimeoutException
{
    public SecurityInferenceTimeoutException(string message) : base(message) { }
}
