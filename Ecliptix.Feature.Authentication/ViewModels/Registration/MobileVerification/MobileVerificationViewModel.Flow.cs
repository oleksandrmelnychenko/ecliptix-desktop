using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    private async Task<Unit> ExecuteVerificationAsync()
    {
        if (_isDisposed)
        {
            return Unit.Default;
        }

        try
        {
            CancellationTokenSource cancellationTokenSource = RecreateCancellationToken(ref _cancellationTokenSource);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
            CancellationToken operationToken = cancellationTokenSource.Token;

            Result<MobileVerificationFlowOutcome, string> flowResult = await _flowCoordinator.ExecuteAsync(
                _flowContext,
                PhonePrefix,
                RawMobileNumber,
                connectId,
                operationToken);

            if (_isDisposed)
            {
                return Unit.Default;
            }

            if (flowResult.IsErr)
            {
                ShowError(flowResult.UnwrapErr());
                return Unit.Default;
            }

            await HandleFlowOutcomeAsync(flowResult.Unwrap());
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            string errorMessage = LocalizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
            Log.Error(ex, "[MOBILE-VERIFICATION] Unexpected error during mobile verification");
            ShowError(errorMessage);
        }

        return Unit.Default;
    }

    private Task HandleFlowOutcomeAsync(MobileVerificationFlowOutcome outcome)
    {
        return outcome switch
        {
            MobileVerificationOtpOutcome otpOutcome => NavigateToOtpVerificationAsync(
                otpOutcome.MobileNumberIdentifier,
                otpOutcome.FullNumber),
            MobileVerificationSecureKeyOutcome => NavigateToSecureKeyAsync(),
            _ => Task.CompletedTask
        };
    }

    private void ShowError(string errorMessage)
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            _executionErrorSubject.OnNext(errorMessage);
        }
    }
}
