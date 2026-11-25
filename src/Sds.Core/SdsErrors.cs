namespace Sds.Core;

/// <summary>Base class of the expected (business) errors raised by SDS services.</summary>
/// <remarks>The API layer maps each subclass to an HTTP status code and a ProblemDetails body.</remarks>
public abstract class SdsException(string message) : Exception(message);

/// <summary>The requested object does not exist (HTTP 404).</summary>
public sealed class SdsNotFoundException(string message) : SdsException(message);

/// <summary>The request conflicts with the current state, for example a duplicate name (HTTP 409).</summary>
public sealed class SdsConflictException(string message) : SdsException(message);

/// <summary>The request is invalid (HTTP 400).</summary>
public sealed class SdsValidationException(string message) : SdsException(message);
