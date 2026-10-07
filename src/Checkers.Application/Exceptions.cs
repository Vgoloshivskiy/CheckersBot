namespace Checkers.Application;

/// <summary>A request the service understood but cannot act on (bad position, bad level, bad limits). Maps to HTTP 422.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>The engine is not running or not ready yet. Maps to HTTP 503.</summary>
public sealed class EngineUnavailableException : Exception
{
    public EngineUnavailableException(string message) : base(message)
    {
    }

    public EngineUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>The engine ran but failed or returned something unusable. Maps to HTTP 500.</summary>
public sealed class EngineFailureException : Exception
{
    public EngineFailureException(string message) : base(message)
    {
    }
}
