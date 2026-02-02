using System;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Google.Protobuf;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    private Task NavigateToOtpVerificationAsync(ByteString mobileNumberIdentifier, string fullNumber)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        if (mobileNumberIdentifier.IsEmpty)
        {
            ShowError(LocalizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
            return Task.CompletedTask;
        }

        VerificationCodeEntryViewModel vm = new(
            _connectivityService,
            NetworkProvider,
            LocalizationService,
            HostScreen,
            (mobileNumberIdentifier, fullNumber),
            _applicationSecureStorageProvider,
            _authRepository,
            GlobalModalService,
            _flowContext);

        if (HostScreen is not AuthenticationViewModel hostWindow)
        {
            return Task.CompletedTask;
        }

        if (_flowContext == AuthenticationFlowContext.REGISTRATION)
        {
            hostWindow.RegistrationMobileNumber = fullNumber;
            _applicationSecureStorageProvider.SetRegistrationMobileNumber(fullNumber);
        }
        else
        {
            hostWindow.RecoveryMobileNumber = fullNumber;
        }

        hostWindow.NavigateToViewModel(vm);

        return Task.CompletedTask;
    }

    private Task NavigateToSecureKeyAsync()
    {
        if (_isDisposed || HostScreen is not AuthenticationViewModel hostWindow)
        {
            return Task.CompletedTask;
        }

        hostWindow.RegistrationMobileNumber = RawMobileNumber;
        hostWindow.Navigate.Execute(MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW).Subscribe();

        return Task.CompletedTask;
    }
}
