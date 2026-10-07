namespace Checkers.Engine.Protocol;

/// <summary>A request the host could not carry out. The message is sent back to the API as the error text.</summary>
public sealed class HostRequestException : Exception
{
    public HostRequestException(string message) : base(message)
    {
    }
}
