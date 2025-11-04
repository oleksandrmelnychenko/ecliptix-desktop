using System;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Google.Protobuf;

namespace Ecliptix.Core.Infrastructure.Data;

internal sealed class PendingLogoutRequestStorage(IApplicationSecureStorageProvider storageProvider)
{
    private const string STORAGE_KEY = "PendingLogout";

    public async Task<Result<Unit, LogoutFailure>> StorePendingLogoutAsync(LogoutRequest request)
    {
        try
        {
            byte[] requestData = request.ToByteArray();

            Result<Unit, InternalServiceApiFailure> storeResult =
                await storageProvider.StoreAsync(STORAGE_KEY, requestData).ConfigureAwait(false);

            if (storeResult.IsErr)
            {
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.UnexpectedError($"Storage failed: {storeResult.UnwrapErr().Message}"));
            }

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Unexpected error: {ex.Message}", ex));
        }
    }

    public async Task<Result<Option<LogoutRequest>, LogoutFailure>> GetPendingLogoutAsync()
    {
        try
        {
            Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                await storageProvider.TryGetByKeyAsync(STORAGE_KEY).ConfigureAwait(false);

            if (getResult.IsErr)
            {
                return Result<Option<LogoutRequest>, LogoutFailure>.Err(
                    LogoutFailure.UnexpectedError($"Storage access failed: {getResult.UnwrapErr().Message}"));
            }

            Option<byte[]> dataOption = getResult.Unwrap();
            if (!dataOption.IsSome)
            {
                return Result<Option<LogoutRequest>, LogoutFailure>.Ok(Option<LogoutRequest>.None);
            }

            LogoutRequest request = LogoutRequest.Parser.ParseFrom(dataOption.Value);
            return Result<Option<LogoutRequest>, LogoutFailure>.Ok(Option<LogoutRequest>.Some(request));
        }
        catch (InvalidProtocolBufferException ex)
        {
            ClearPendingLogout();
            return Result<Option<LogoutRequest>, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Failed to parse stored request: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<Option<LogoutRequest>, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Unexpected error: {ex.Message}", ex));
        }
    }

    public void ClearPendingLogout() => storageProvider.Delete(STORAGE_KEY);
}
