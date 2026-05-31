namespace AiUsageBar.Core.Errors;

/// <summary>Base for all domain errors. Port of the Rust <c>AppError</c> enum.</summary>
public abstract class AppException(string message) : Exception(message)
{
    /// <summary>
    /// True for errors that should resolve themselves (DNS/timeout/connection).
    /// Transient errors show a quiet "Loading…" rather than an HTTP code.
    /// </summary>
    public virtual bool IsTransient => false;
}

/// <summary>HTTP non-2xx response — carries the status code and a short body.</summary>
public sealed class HttpStatusException(int status, string body)
    : AppException($"HTTP {status}: {body}")
{
    public int Status { get; } = status;
    public string Body { get; } = body;
}

/// <summary>Network-level failure (DNS, timeout, connection refused). Transient.</summary>
public sealed class TransportException(string message) : AppException(message)
{
    public override bool IsTransient => true;
}

/// <summary>Credential read/refresh failure — usually "re-auth required".</summary>
public sealed class CredentialsException(string message) : AppException(message);

/// <summary>Response did not match the expected schema.</summary>
public sealed class SchemaException(string message) : AppException(message);

/// <summary>Catch-all for anything else.</summary>
public sealed class OtherException(string message) : AppException(message);
