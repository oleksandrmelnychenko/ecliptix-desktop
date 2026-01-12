using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    public void ResetState()
    {
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);
        _verifySecureKeyBuffer.Remove(0, _verifySecureKeyBuffer.Length);
        _hasSecureKeyBeenTouched = false;
        _hasVerifySecureKeyBeenTouched = false;

        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        VerifySecureKeyError = string.Empty;
        HasVerifySecureKeyError = false;

        ServerError = string.Empty;
        HasServerError = false;

        IsMembershipLoading = true;
        MembershipUniqueId = null;

        SetServerError(string.Empty);
    }

    private async Task<Result<Unit, InternalServiceApiFailure>> LoadMembershipAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> applicationInstance =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (applicationInstance.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(applicationInstance.UnwrapErr());
        }

        ApplicationInstanceSettings settings = applicationInstance.Unwrap();

        if (settings.Membership == null)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(
                    "Membership data is not available. Please complete registration from the beginning."));
        }

        if (settings.Membership.MembershipId == null || settings.Membership.MembershipId.IsEmpty)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(
                    "Membership unique identifier is missing. Please complete registration from the beginning."));
        }

        MembershipUniqueId = settings.Membership.MembershipId;
        return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelCurrentOperation();
            _secureKeyBuffer.Dispose();
            _verifySecureKeyBuffer.Dispose();
            _executionErrorSubject.Dispose();
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(
            ref _currentOperationCts,
            null);

        if (operationSource == null)
        {
            return;
        }

        try
        {
            operationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {

        }
        finally
        {
            operationSource.Dispose();
        }
    }
}
