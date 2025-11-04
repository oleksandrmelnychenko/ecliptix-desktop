using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Utilities.Failures.Sodium;

public static class SodiumFailureExtensions
{
    public static EcliptixProtocolFailure ToEcliptixProtocolFailure(this SodiumFailure sodiumFailure)
    {
        return sodiumFailure.Type switch
        {
            SodiumFailureType.INITIALIZATION_FAILED => EcliptixProtocolFailure.Generic(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.LibraryNotFound => EcliptixProtocolFailure.Generic(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.ALLOCATION_FAILED => EcliptixProtocolFailure.ALLOCATION_FAILED(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.MemoryPinningFailed => EcliptixProtocolFailure.PinningFailure(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.SECURE_WIPE_FAILED => EcliptixProtocolFailure.MemoryBufferError(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.MemoryProtectionFailed => EcliptixProtocolFailure.MemoryBufferError(sodiumFailure.Message,
                sodiumFailure.InnerException),
            SodiumFailureType.NullPointer => EcliptixProtocolFailure.OBJECT_DISPOSED(sodiumFailure.Message),
            SodiumFailureType.InvalidBufferSize => EcliptixProtocolFailure.InvalidInput(sodiumFailure.Message),
            SodiumFailureType.BUFFER_TOO_SMALL => EcliptixProtocolFailure.BUFFER_TOO_SMALL(sodiumFailure.Message),
            SodiumFailureType.BUFFER_TOO_LARGE => EcliptixProtocolFailure.DATA_TOO_LARGE(sodiumFailure.Message),
            _ => EcliptixProtocolFailure.Generic(sodiumFailure.Message, sodiumFailure.InnerException)
        };
    }
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
