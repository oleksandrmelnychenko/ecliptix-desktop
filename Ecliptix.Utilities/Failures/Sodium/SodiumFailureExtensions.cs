using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Utilities.Failures.Sodium;

public static class SodiumFailureExtensions
{
    public static EcliptixProtocolFailure ToEcliptixProtocolFailure(this SodiumFailure sodiumFailure) =>
        sodiumFailure.FailureType switch
        {
            SodiumFailureType.INITIALIZATION_FAILED => EcliptixProtocolFailure.Generic(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.LIBRARY_NOT_FOUND => EcliptixProtocolFailure.Generic(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.ALLOCATION_FAILED => EcliptixProtocolFailure.AllocationFailed(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.MEMORY_PINNING_FAILED => EcliptixProtocolFailure.PinningFailure(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.SECURE_WIPE_FAILED => EcliptixProtocolFailure.MemoryBufferError(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.MEMORY_PROTECTION_FAILED => EcliptixProtocolFailure.MemoryBufferError(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.NULL_POINTER => EcliptixProtocolFailure.ObjectDisposed(sodiumFailure.Message),
            SodiumFailureType.INVALID_BUFFER_SIZE => EcliptixProtocolFailure.InvalidInput(sodiumFailure.Message),
            SodiumFailureType.BUFFER_TOO_SMALL => EcliptixProtocolFailure.BufferTooSmall(sodiumFailure.Message),
            SodiumFailureType.BUFFER_TOO_LARGE => EcliptixProtocolFailure.DataTooLarge(sodiumFailure.Message),
            _ => EcliptixProtocolFailure.Generic(sodiumFailure.Message, sodiumFailure.InnerException)
        };
}

public static class ResultSodiumExtensions
{
    public static Result<T, EcliptixProtocolFailure> MapSodiumFailure<T>(this Result<T, SodiumFailure> result)
    {
        return result.IsOk
            ? Result<T, EcliptixProtocolFailure>.Ok(result.Unwrap())
            : Result<T, EcliptixProtocolFailure>.Err(result.UnwrapErr().ToEcliptixProtocolFailure());
    }
}
