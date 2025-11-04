namespace Ecliptix.Utilities.Failures.Sodium;

public sealed class SodiumFailure
{
    private SodiumFailure(SodiumFailureType type, string message, Exception? innerException = null)
    {
        Type = type;
        Message = message;
        InnerException = innerException;
    }

    public SodiumFailureType Type { get; }
    public string Message { get; }
    public Exception? InnerException { get; }

    public static SodiumFailure INITIALIZATION_FAILED(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.INITIALIZATION_FAILED, details, inner);
    }

    public static SodiumFailure ComparisonFailed(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.ComparisonFailed, details, inner);
    }

    public static SodiumFailure LibraryNotFound(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.LibraryNotFound, details, inner);
    }

    public static SodiumFailure ALLOCATION_FAILED(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.ALLOCATION_FAILED, details, inner);
    }

    public static SodiumFailure MemoryPinningFailed(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.MemoryPinningFailed, details, inner);
    }

    public static SodiumFailure SECURE_WIPE_FAILED(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.SECURE_WIPE_FAILED, details, inner);
    }

    public static SodiumFailure MemoryProtectionFailed(string details, Exception? inner = null)
    {
        return new SodiumFailure(SodiumFailureType.MemoryProtectionFailed, details, inner);
    }

    public static SodiumFailure NullPointer(string details)
    {
        return new SodiumFailure(SodiumFailureType.NullPointer, details);
    }

    public static SodiumFailure InvalidBufferSize(string details)
    {
        return new SodiumFailure(SodiumFailureType.InvalidBufferSize, details);
    }

    public static SodiumFailure BUFFER_TOO_SMALL(string details)
    {
        return new SodiumFailure(SodiumFailureType.BUFFER_TOO_SMALL, details);
    }

    public static SodiumFailure BUFFER_TOO_LARGE(string details)
    {
        return new SodiumFailure(SodiumFailureType.BUFFER_TOO_LARGE, details);
    }

    public static SodiumFailure InvalidOperation(string details)
    {
        return new SodiumFailure(SodiumFailureType.InvalidBufferSize, details);
    }

    public static SodiumFailure OBJECT_DISPOSED(string details)
    {
        return new SodiumFailure(SodiumFailureType.NullPointer, details);
    }

    public override string ToString()
    {
        return
            $"SodiumFailure(Type={Type}, Message='{Message}'{(InnerException != null ? $", InnerException='{InnerException.GetType().Name}: {InnerException.Message}'" : "")})";
    }

    public override bool Equals(object? obj)
    {
        return obj is SodiumFailure other &&
               Type == other.Type &&
               Message == other.Message &&
               Equals(InnerException, other.InnerException);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Message, InnerException);
    }
}
