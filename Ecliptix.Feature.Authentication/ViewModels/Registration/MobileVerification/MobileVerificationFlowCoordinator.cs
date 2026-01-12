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
        string fullNumber = PhoneNumberHelper.CombineWithPrefix(phonePrefix, rawMobileNumber);

        Result<ValidateMobileNumberResponse, string> validationResult =
            await authRepository.ValidateMobileNumberAsync(fullNumber, connectId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (validationResult.IsErr)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(validationResult.UnwrapErr());
        }

        ValidateMobileNumberResponse validateMobileNumberResponse = validationResult.Unwrap();

        if (validateMobileNumberResponse.Result != VerificationResult.Succeeded)
        {
            string errorMessage = !string.IsNullOrEmpty(validateMobileNumberResponse.Message)
                ? validateMobileNumberResponse.Message
                : localizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
            return Result<MobileVerificationFlowOutcome, string>.Err(errorMessage);
        }

        if (validateMobileNumberResponse.MobileNumberIdentifier.IsEmpty)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        return await HandleMobileAvailabilityCheckAsync(
            validateMobileNumberResponse.MobileNumberIdentifier,
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
        Result<CheckMobileNumberAvailabilityResponse, string> statusResult =
            await authRepository.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId,
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (statusResult.IsErr)
        {
            return Result<MobileVerificationFlowOutcome, string>.Err(statusResult.UnwrapErr());
        }

        CheckMobileNumberAvailabilityResponse statusResponse = statusResult.Unwrap();
        return await HandleAvailabilityStatusAsync(statusResponse, mobileNumberIdentifier, fullNumber,
            cancellationToken);
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> HandleAvailabilityStatusAsync(
        CheckMobileNumberAvailabilityResponse statusResponse,
        ByteString mobileNumberIdentifier,
        string fullNumber,
        CancellationToken cancellationToken)
    {
        return statusResponse.Status switch
        {
            MobileAvailabilityStatus.Available or MobileAvailabilityStatus.RegistrationExpired =>
                Result<MobileVerificationFlowOutcome, string>.Ok(
                    new MobileVerificationOtpOutcome(fullNumber, mobileNumberIdentifier)),
            MobileAvailabilityStatus.IncompleteRegistration => await HandleIncompleteRegistrationAsync(statusResponse,
                mobileNumberIdentifier, fullNumber, cancellationToken),
            MobileAvailabilityStatus.DataCorruption => Result<MobileVerificationFlowOutcome, string>.Err(
                ResolveLocalization(statusResponse.LocalizationKey, "MobileVerification.ERROR.DataCorruption")),
            _ => Result<MobileVerificationFlowOutcome, string>.Err(ResolveLocalization(statusResponse.LocalizationKey,
                "MobileVerification.ERROR.MobileAlreadyRegistered"))
        };
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> HandleIncompleteRegistrationAsync(
        CheckMobileNumberAvailabilityResponse statusResponse,
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
        CheckMobileNumberAvailabilityResponse statusResponse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MembershipProto membership = new()
        {
            UniqueIdentifier = statusResponse.ExistingMembershipId,
            Status = statusResponse.HasActivityStatus
                ? statusResponse.ActivityStatus
                : MembershipActivityStatus.Active,
            CreationStatus = statusResponse.CreationStatus
        };

        if (statusResponse.AccountUniqueIdentifier != null && !statusResponse.AccountUniqueIdentifier.IsEmpty)
        {
            membership.AccountUniqueIdentifier = statusResponse.AccountUniqueIdentifier;
        }

        await applicationSecureStorageProvider.SetApplicationMembershipAsync(membership.UniqueIdentifier);

        if (statusResponse.AccountUniqueIdentifier != null && !statusResponse.AccountUniqueIdentifier.IsEmpty)
        {
            await applicationSecureStorageProvider.SetCurrentAccountIdAsync(statusResponse.AccountUniqueIdentifier)
                .ConfigureAwait(false);
        }
    }

    private async Task<Result<MobileVerificationFlowOutcome, string>> ExecuteRecoveryFlowAsync(
        string phonePrefix,
        string rawMobileNumber,
        uint connectId,
        CancellationToken cancellationToken)
    {
        string fullNumber = PhoneNumberHelper.CombineWithPrefix(phonePrefix, rawMobileNumber);

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
