using Grpc.Core;

namespace Ecliptix.Utilities.Failures.RpcMetaData;

public sealed record MetaDataSystemFailure(
    MetaDataSystemFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog() => new
    {
        MetaDataSystemFailureType = FailureType.ToString(),
        Message,
        InnerException = InnerException?.Message,
        Timestamp
    };

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            MetaDataSystemFailureType.REQUIRED_COMPONENT_NOT_FOUND => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            MetaDataSystemFailureType.OPTIONAL => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL)
        };

    public static MetaDataSystemFailure ComponentNotFound(string? details = null) =>
        new(MetaDataSystemFailureType.REQUIRED_COMPONENT_NOT_FOUND, details ?? "Required component not found");
}
