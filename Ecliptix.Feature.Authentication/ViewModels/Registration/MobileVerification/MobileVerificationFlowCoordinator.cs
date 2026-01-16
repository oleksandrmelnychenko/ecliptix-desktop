using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using MembershipActivityStatus = Ecliptix.Protobuf.Membership.Membership.Types.ActivityStatus;
using MembershipCreationStatus = Ecliptix.Protobuf.Membership.Membership.Types.CreationStatus;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

internal abstract record MobileVerificationFlowOutcome(string FullNumber);

internal sealed record MobileVerificationOtpOutcome(string FullNumber, ByteString MobileNumberIdentifier)
    : MobileVerificationFlowOutcome(FullNumber);

internal sealed record MobileVerificationSecureKeyOutcome(string FullNumber)
    : MobileVerificationFlowOutcome(FullNumber);

internal sealed class MobileVerificationFlowCoordinator(
    IAuthRepository authRepository,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ILocalizationService localizationService)
{
    public async Task<Result<MobileVerificationFlowOutcome, string>> ExecuteAsync(
        AuthenticationFlowContext flowContext,
        string phonePrefix,
        string rawMobileNumber,
        uint connectId,
        CancellationToken cancellationToken)
    {
        return flowContext == AuthenticationFlowContext.REGISTRATION
            ? await ExecuteRegistrationFlowAsync(phonePrefix, rawMobileNumber, connectId, cancellationToken)
            : await ExecuteRecoveryFlowAsync(phonePrefix, rawMobileNumber, connectId, cancellationToken);
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> ExecuteRegistrationFlowAsync(
        string phonePrefix,
        string rawMobileNumber,
        uint connectId,
        CancellationToken cancellationToken)
    {
        string fullNumber = MobileNumberHelper.CombineWithPrefix(phonePrefix, rawMobileNumber);

        Result<MobileNumberValidateResponse, string> validationResult =
            await authRepository.ValidateMobileNumberAsync(fullNumber, connectId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (validationResult.IsErr)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(validationResult.UnwrapErr());
        }

        MobileNumberValidateResponse validateMobileNumberResponse = validationResult.Unwrap();

        if (validateMobileNumberResponse.Result != OtpVerificationResult.Succeeded)
        {
            string errorMessage = !string.IsNullOrEmpty(validateMobileNumberResponse.Message)
                ? validateMobileNumberResponse.Message
                : localizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
            return Result<MobileVerificationFlowOutcome, string>.Err(errorMessage);
        }

        if (validateMobileNumberResponse.MobileNumberId.IsEmpty)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        return await HandleMobileAvailabilityCheckAsync(
            validateMobileNumberResponse.MobileNumberId,
            fullNumber,
            connectId,
            cancellationToken);
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> HandleMobileAvailabilityCheckAsync(
        ByteString mobileNumberIdentifier,
        string fullNumber,
        uint connectId,
        CancellationToken cancellationToken)
    {
        Result<MobileNumberAvailabilityResponse, string> statusResult =
            await authRepository.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId,
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (statusResult.IsErr)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(statusResult.UnwrapErr());
        }

        MobileNumberAvailabilityResponse statusResponse = statusResult.Unwrap();
        return await HandleAvailabilityStatusAsync(statusResponse, mobileNumberIdentifier, fullNumber,
            cancellationToken);
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> HandleAvailabilityStatusAsync(
        MobileNumberAvailabilityResponse statusResponse,
        ByteString mobileNumberIdentifier,
        string fullNumber,
        CancellationToken cancellationToken)
    {
        if (statusResponse is { CanRegister: false, CanContinue: false })
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(
                ResolveLocalization(statusResponse.LocalizationKey, "MobileVerification.ERROR.MobileAlreadyRegistered"));
        }

        return statusResponse.Status switch
        {
            MobileNumberAvailabilityStatus.MobileNumberAvailabilityAvailable or MobileNumberAvailabilityStatus.MobileNumberAvailabilityRegistrationExpired =>
                Result<MobileVerificationFlowOutcome, string>.Ok(
                    new MobileVerificationOtpOutcome(fullNumber, mobileNumberIdentifier)),
            MobileNumberAvailabilityStatus.MobileNumberAvailabilityIncompleteRegistration => await HandleIncompleteRegistrationAsync(statusResponse,
                mobileNumberIdentifier, fullNumber, cancellationToken),
            MobileNumberAvailabilityStatus.MobileNumberAvailabilityDataCorruption => Result<MobileVerificationFlowOutcome, string>.Err(
                ResolveLocalization(statusResponse.LocalizationKey, "MobileVerification.ERROR.DataCorruption")),
            _ => Result<MobileVerificationFlowOutcome, string>.Err(ResolveLocalization(statusResponse.LocalizationKey,
                "MobileVerification.ERROR.MobileAlreadyRegistered"))
        };
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> HandleIncompleteRegistrationAsync(
        MobileNumberAvailabilityResponse statusResponse,
        ByteString mobileNumberIdentifier,
        string fullNumber,
        CancellationToken cancellationToken)
    {
        if (statusResponse is
            not
            {
                HasCreationStatus: true, CreationStatus: MembershipCreationStatus.OtpVerified
            })
        {
            return Result<MobileVerificationFlowOutcome, string>.Ok(
                new MobileVerificationOtpOutcome(fullNumber, mobileNumberIdentifier));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await StoreIncompleteMembershipAsync(statusResponse, cancellationToken);
        return Result<MobileVerificationFlowOutcome, string>.Ok(new MobileVerificationSecureKeyOutcome(fullNumber));

    }

    private async Task StoreIncompleteMembershipAsync(
        MobileNumberAvailabilityResponse statusResponse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MembershipProto membership = new()
        {
            MembershipId = statusResponse.ExistingMembershipId,
            Status = statusResponse.HasActivityStatus
                ? statusResponse.ActivityStatus
                : MembershipActivityStatus.Active,
            CreationStatus = statusResponse.CreationStatus
        };

        if (statusResponse.AccountId != null && !statusResponse.AccountId.IsEmpty)
        {
            membership.AccountId = statusResponse.AccountId;
        }

        await applicationSecureStorageProvider.SetApplicationMembershipAsync(membership.MembershipId);

        if (statusResponse.AccountId != null && !statusResponse.AccountId.IsEmpty)
        {
            await applicationSecureStorageProvider.SetCurrentAccountIdAsync(statusResponse.AccountId)
                .ConfigureAwait(false);
        }
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> ExecuteRecoveryFlowAsync(
        string phonePrefix,
        string rawMobileNumber,
        uint connectId,
        CancellationToken cancellationToken)
    {
        string fullNumber = MobileNumberHelper.CombineWithPrefix(phonePrefix, rawMobileNumber);

        Result<ByteString, string> recoveryValidationResult =
            await authRepository.ValidateMobileForRecoveryAsync(fullNumber, connectId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (recoveryValidationResult.IsErr)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(recoveryValidationResult.UnwrapErr());
        }

        ByteString mobileNumberIdentifier = recoveryValidationResult.Unwrap();

        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        return Result<MobileVerificationFlowOutcome, string>.Ok(
            new MobileVerificationOtpOutcome(fullNumber, mobileNumberIdentifier));
    }

    private string ResolveLocalization(string localizationKey, string fallbackKey)
    {
        return !string.IsNullOrEmpty(localizationKey)
            ? localizationService[localizationKey]
            : localizationService[fallbackKey];
    }
}
