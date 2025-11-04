using System;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Data;

internal sealed class PendingLogoutRequestStorage
{
    private const string STORAGE_KEY = "PendingLogout";
    private readonly IApplicationSecureStorageProvider _storageProvider;

    public PendingLogoutRequestStorage(IApplicationSecureStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    public async Task<Result<Unit, LogoutFailure>> StorePendingLogoutAsync(LogoutRequest request)
    {
        try
        {
            byte[] requestData = request.ToByteArray();

            Result<Unit, InternalServiceApiFailure> storeResult =
                await _storageProvider.StoreAsync(STORAGE_KEY, requestData).ConfigureAwait(false);

            if (storeResult.IsErr)
            {
                Log.Warning("[PENDING-LOGOUT] Failed to store pending logout request");
                return Result<Unit, LogoutFailure>.Err(
                    LogoutFailure.UnexpectedError($"Storage failed: {storeResult.UnwrapErr().Message}"));
            }

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PENDING-LOGOUT] Unexpected error storing pending logout request");
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Unexpected error: {ex.Message}", ex));
        }
    }

    public async Task<Result<Option<LogoutRequest>, LogoutFailure>> GetPendingLogoutAsync()
    {
        try
        {
            Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                await _storageProvider.TryGetByKeyAsync(STORAGE_KEY).ConfigureAwait(false);

            if (getResult.IsErr)
            {
                Log.Warning("[PENDING-LOGOUT] Failed to get pending logout request");
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
            Log.Error(ex, "[PENDING-LOGOUT] Failed to parse stored logout request");
            CLEAR_PENDING_LOGOUT();
            return Result<Option<LogoutRequest>, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Failed to parse stored request: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PENDING-LOGOUT] Unexpected error getting pending logout request");
            return Result<Option<LogoutRequest>, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError($"Unexpected error: {ex.Message}", ex));
        }
    }

    public void CLEAR_PENDING_LOGOUT()
    {
        try
        {
            _storageProvider.Delete(STORAGE_KEY);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PENDING-LOGOUT] Failed to clear pending logout request");
        }
    }
}
