using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    private async Task<SystemU> SubmitAsync()
    {
        if (IsBusy || !CanSubmit)
        {
            return SystemU.Default;
        }

        try
        {
            CancellationTokenSource operationCts = RecreateCancellationToken(ref _currentOperationCts);
            CancellationToken operationToken = operationCts.Token;

            try
            {
                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

                Task<Result<Unit, string>> completeTask = _flowContext == AuthenticationFlowContext.REGISTRATION
                    ? CompleteRegistrationAsync(connectId, operationToken)
                    : CompleteSecureKeyResetAsync(connectId, operationToken);

                Result<Unit, string> result = await completeTask;

                if (result.IsErr)
                {
                    SetServerError(result.UnwrapErr());
                    return SystemU.Default;
                }

                if (HostScreen is not AuthenticationViewModel hostViewModel)
                {
                    return SystemU.Default;
                }

                if (_flowContext == AuthenticationFlowContext.REGISTRATION)
                {
                    string? memoryNumber = hostViewModel.RegistrationMobileNumber;
                    Option<string> mobileNumberOpt = string.IsNullOrEmpty(memoryNumber)
                        ? Option<string>.None
                        : Option<string>.Some(memoryNumber);

                    if (!mobileNumberOpt.IsSome)
                    {
                        Result<ApplicationInstanceSettings, InternalServiceApiFailure> storageResult =
                            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

                        if (storageResult.IsOk)
                        {
                            string storedNumber = storageResult.Unwrap().RegistrationMobileNumber;

                            if (!string.IsNullOrEmpty(storedNumber))
                            {
                                hostViewModel.RegistrationMobileNumber = storedNumber;
                                mobileNumberOpt = Option<string>.Some(storedNumber);
                            }
                        }
                    }

                    if (mobileNumberOpt.IsSome)
                    {
                        bool signedIn = await SignInAsync(mobileNumberOpt.Value!, connectId, operationToken, navigateToMain: false);

                        if (signedIn)
                        {
                            await _applicationSecureStorageProvider.SetRegistrationMobileNumber(string.Empty);

                            hostViewModel.Navigate.Execute(MembershipViewType.COMPLETE_PROFILE_VIEW).Subscribe();
                        }
                    }
                    else
                    {
                        string errorMsg = LocalizationService[AuthenticationConstants.NO_VERIFICATION_SESSION_KEY]
                                          ?? "Session data missing. Please restart registration.";
                        SetServerError(errorMsg);
                        // TODO consider what type of navigation to use, with redirect notification or silent navigation
                        ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                        ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();

                    }

                    return SystemU.Default;
                }

                Option<string> mobileNumberOption = Option<string>.Some(hostViewModel.RecoveryMobileNumber!);

                if (mobileNumberOption.IsSome)
                {
                    await SignInAsync(mobileNumberOption.Value!, connectId, operationToken);
                }

                return SystemU.Default;
            }
            finally
            {
                if (_currentOperationCts != null && ReferenceEquals(_currentOperationCts, operationCts))
                {
                    _currentOperationCts = null;
                }

                operationCts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return SystemU.Default;
        }
    }

    private async Task<Result<Unit, string>> CompleteRegistrationAsync(uint connectId,
        CancellationToken cancellationToken)
    {
        if (MembershipUniqueId == null)
        {
            return Result<Unit, string>.Err(
                LocalizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        return await _authRepository.CompleteRegistrationAsync(
            MembershipUniqueId,
            _secureKeyBuffer,
            connectId,
            cancellationToken);
    }

    private async Task<Result<Unit, string>> CompleteSecureKeyResetAsync(uint connectId,
        CancellationToken cancellationToken)
    {
        if (MembershipUniqueId == null)
        {
            return Result<Unit, string>.Err(
                LocalizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        return await _authRepository.CompleteSecureKeyResetAsync(
            MembershipUniqueId,
            _secureKeyBuffer,
            connectId,
            cancellationToken);
    }

    private async Task<bool> SignInAsync(string mobileNumber, uint connectId, CancellationToken cancellationToken, bool navigateToMain = true)
    {
        Result<Unit, AuthenticationFailure> signInResult = await _authRepository.SignInAsync(
            mobileNumber,
            _secureKeyBuffer,
            connectId,
            cancellationToken);

        if (signInResult.IsOk)
        {
            if (navigateToMain && HostScreen is AuthenticationViewModel hostViewModel)
            {
                try
                {
                    await hostViewModel.SwitchToMainWindowCommand.Execute();
                }
                catch
                {
                    SetServerError($"{LocalizationService[AuthenticationConstants.NAVIGATION_FAILURE_KEY]}");
                }
            }
            return true;
        }

        if (!signInResult.IsErr)
        {
            return false;
        }

        AuthenticationFailure failure = signInResult.UnwrapErr();
        SetServerError(failure.Message);

        return false;
    }
}
