using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Validations;

namespace Ecliptix.Utilities.Failures;

public static class ResultMappingExtensions
{
    public static Result<T, NetworkFailure> ToNetworkFailure<T>(
        this Result<T, EcliptixProtocolFailure> sourceResult)
    {
        if (!sourceResult.IsErr) return Result<T, NetworkFailure>.Ok(sourceResult.Unwrap());
        EcliptixProtocolFailure protocolFailure = sourceResult.UnwrapErr();
            
        NetworkFailure networkFailure = protocolFailure.ToNetworkFailure();
            
        return Result<T, NetworkFailure>.Err(networkFailure);
    }

    public static Result<T, ValidationFailure> ToValidationFailure<T>(
        this Result<T, NetworkFailure> sourceResult)
    {
        if (!sourceResult.IsErr) return Result<T, ValidationFailure>.Ok(sourceResult.Unwrap());
        NetworkFailure networkFailure = sourceResult.UnwrapErr();
            
        ValidationFailure validationFailure = ValidationFailure.SignInFailed(
            networkFailure.Message, networkFailure.InnerException);
            
        return Result<T, ValidationFailure>.Err(validationFailure);
    }
}
