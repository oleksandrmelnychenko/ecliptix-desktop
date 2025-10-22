using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Utilities.Failures;

public static class ResultMappingExtensions
{
    public static Result<T, NetworkFailure> ToNetworkFailure<T>(
        this Result<T, EcliptixProtocolFailure> sourceResult)
    {
        if (!sourceResult.IsErr)
        {
            return Result<T, NetworkFailure>.Ok(sourceResult.Unwrap());
        }

        EcliptixProtocolFailure protocolFailure = sourceResult.UnwrapErr();

        NetworkFailure networkFailure = protocolFailure.ToNetworkFailure();

        return Result<T, NetworkFailure>.Err(networkFailure);
    }
}
