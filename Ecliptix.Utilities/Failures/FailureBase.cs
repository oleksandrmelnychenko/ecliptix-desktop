using Grpc.Core;

namespace Ecliptix.Utilities.Failures;

public interface IFailureBase
{
    object ToStructuredLog();
}

public abstract record FailureBase(string Message, Exception? InnerException = null) : IFailureBase
{
    protected DateTime Timestamp { get; } = DateTime.UtcNow;

    public abstract object ToStructuredLog();
}