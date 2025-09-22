using System;

namespace Ecliptix.Opaque.Protocol;

public sealed class OpaqueFailure
{
    public string Message { get; }

    private OpaqueFailure(string message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public static OpaqueFailure InvalidInput(string message) => new($"Invalid input: {message}");
    public static OpaqueFailure CryptoError(string message) => new($"Cryptographic error: {message}");
    public static OpaqueFailure MacVerificationFailed(string message) => new($"MAC verification failed: {message}");
    public static OpaqueFailure MemoryError(string message) => new($"Memory error: {message}");
    public static OpaqueFailure AuthenticationFailed(string message) => new($"Authentication failed: {message}");
}