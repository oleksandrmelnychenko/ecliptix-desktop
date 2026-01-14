using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Sodium;

public sealed record SodiumFailure(
    SodiumFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog() => new
    {
        SodiumFailureType = FailureType.ToString(),
        Message,
        InnerException = InnerException?.Message,
        Timestamp
    };

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            SodiumFailureType.INITIALIZATION_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            SodiumFailureType.LIBRARY_NOT_FOUND => new GrpcErrorDescriptor(
                ErrorCode.DEPENDENCY_UNAVAILABLE, StatusCode.Unavailable, ErrorI18NKeys.DEPENDENCY_UNAVAILABLE),
            SodiumFailureType.ALLOCATION_FAILED => new GrpcErrorDescriptor(
                ErrorCode.RESOURCE_EXHAUSTED, StatusCode.ResourceExhausted, ErrorI18NKeys.RESOURCE_EXHAUSTED),
            SodiumFailureType.MEMORY_PINNING_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            SodiumFailureType.SECURE_WIPE_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            SodiumFailureType.INVALID_BUFFER_SIZE => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            SodiumFailureType.BUFFER_TOO_SMALL => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            SodiumFailureType.BUFFER_TOO_LARGE => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            SodiumFailureType.NULL_POINTER => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            SodiumFailureType.MEMORY_PROTECTION_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            SodiumFailureType.COMPARISON_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL)
        };

    public static SodiumFailure InitializationFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.INITIALIZATION_FAILED, details, inner);

    public static SodiumFailure ComparisonFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.COMPARISON_FAILED, details, inner);

    public static SodiumFailure LibraryNotFound(string details, Exception? inner = null) =>
        new(SodiumFailureType.LIBRARY_NOT_FOUND, details, inner);

    public static SodiumFailure AllocationFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.ALLOCATION_FAILED, details, inner);

    public static SodiumFailure MemoryPinningFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.MEMORY_PINNING_FAILED, details, inner);

    public static SodiumFailure SecureWipeFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.SECURE_WIPE_FAILED, details, inner);

    public static SodiumFailure MemoryProtectionFailed(string details, Exception? inner = null) =>
        new(SodiumFailureType.MEMORY_PROTECTION_FAILED, details, inner);

    public static SodiumFailure NullPointer(string details) =>
        new(SodiumFailureType.NULL_POINTER, details);

    public static SodiumFailure InvalidBufferSize(string details) =>
        new(SodiumFailureType.INVALID_BUFFER_SIZE, details);

    public static SodiumFailure BufferTooSmall(string details) =>
        new(SodiumFailureType.BUFFER_TOO_SMALL, details);

    public static SodiumFailure BufferTooLarge(string details) =>
        new(SodiumFailureType.BUFFER_TOO_LARGE, details);

    public static SodiumFailure InvalidOperation(string details) =>
        new(SodiumFailureType.INVALID_BUFFER_SIZE, details);

    public static SodiumFailure ObjectDisposed(string details) =>
        new(SodiumFailureType.NULL_POINTER, details);
}
