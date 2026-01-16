using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Serilog;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

public sealed class IdentityService : IIdentityService
{
    private const byte MASTER_KEY_SHARE_THRESHOLD = 2;
    private const byte MASTER_KEY_SHARE_COUNT = 3;

    private readonly ISecureProtocolStateStorage _storage;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;

    public IdentityService(
        ISecureProtocolStateStorage storage,
        IApplicationSecureStorageProvider applicationSecureStorageProvider)
    {
        _storage = storage;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
    }

    public async Task<bool> HasStoredIdentityAsync(string accountId)
    {
        IdentityContext context = new(accountId);

        foreach (string shareKey in context.ShareStorageKeys)
        {
            Result<byte[], SecureStorageFailure> shareResult =
                await _storage.LoadStateAsync(shareKey, context.AccountBytes).ConfigureAwait(false);

            if (shareResult.IsOk)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string accountId)
    {
        IdentityContext context = new(accountId);
        byte[]? originalMasterKeyBytes = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to read master key for storage: {readResult.UnwrapErr().Message}"));
            }

            originalMasterKeyBytes = readResult.Unwrap();

            Result<Unit, AuthenticationFailure> shareStoreResult =
                await StoreMasterKeySharesAsync(originalMasterKeyBytes, context).ConfigureAwait(false);
            if (shareStoreResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(shareStoreResult.UnwrapErr());
            }

            Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult = await LoadMasterKeyAsync(context).ConfigureAwait(false);

            if (loadResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not reconstruct stored master key: {loadResult.UnwrapErr().Message}"));
            }

            using SodiumSecureMemoryHandle loadedKeyHandle = loadResult.Unwrap();
            Result<byte[], SodiumFailure> loadedReadResult = loadedKeyHandle.ReadBytes(loadedKeyHandle.Length);

            if (loadedReadResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not read loaded master key: {loadedReadResult.UnwrapErr().Message}"));
            }

            byte[] loadedMasterKeyBytes = loadedReadResult.Unwrap();

            if (!CryptographicOperations.FixedTimeEquals(originalMasterKeyBytes, loadedMasterKeyBytes))
            {
                CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed("Master key verification failed"));
            }

            CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to store/verify master key: {ex.Message}", ex));
        }
        finally
        {
            if (originalMasterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(originalMasterKeyBytes);
            }
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string accountId) =>
        await LoadMasterKeyAsync(new IdentityContext(accountId)).ConfigureAwait(false);

    public async Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string accountId)
    {
        IdentityContext context = new(accountId);

        try
        {
            foreach (string shareKey in context.ShareStorageKeys)
            {
                Result<Unit, SecureStorageFailure> deleteShareResult =
                    await _storage.DeleteStateAsync(shareKey).ConfigureAwait(false);

                if (deleteShareResult.IsErr)
                {
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.IdentityStorageFailed(
                            $"Failed to delete master key share from storage: {deleteShareResult.UnwrapErr().Message}"));
                }
            }

            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to clear identity cache: {ex.Message}", ex));
        }
    }

    public async Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string accountId, uint connectId)
    {
        Result<Unit, SecureStorageFailure> deleteResult =
            await _storage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }

        Result<Unit, AuthenticationFailure> clearResult =
            await ClearAllCacheAsync(accountId).ConfigureAwait(false);

        if (clearResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL] Identity cache clear failed: {Error}",
                clearResult.UnwrapErr().Message);
        }

        Result<Unit, InternalServiceApiFailure> membershipClearResult =
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);
        if (membershipClearResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL] Failed to clear membership state: {Error}",
                membershipClearResult.UnwrapErr().Message);
        }

        return Result<Unit, Exception>.Ok(Unit.Value);
    }

    private async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyAsync(IdentityContext context)
    {
        try
        {
            Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure> shareResult =
                await TryLoadMasterKeyFromSharesAsync(context).ConfigureAwait(false);
            if (shareResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(shareResult.UnwrapErr());
            }

            Option<SodiumSecureMemoryHandle> shareHandleOption = shareResult.Unwrap();
            if (shareHandleOption.IsSome)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(shareHandleOption.Value!);
            }
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed("Master key shares not found"));
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to load master key: {ex.Message}", ex));
        }
    }

    private async Task<Result<Unit, AuthenticationFailure>> StoreMasterKeySharesAsync(
        byte[] masterKeyBytes,
        IdentityContext context)
    {
        Result<byte[][], EcliptixProtocolFailure> splitResult =
            ShamirSecretSharing.Split(masterKeyBytes, MASTER_KEY_SHARE_THRESHOLD, MASTER_KEY_SHARE_COUNT);
        if (splitResult.IsErr)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed(
                    $"Failed to split master key: {splitResult.UnwrapErr().Message}"));
        }

        byte[][] shares = splitResult.Unwrap();
        try
        {
            if (shares.Length != MASTER_KEY_SHARE_COUNT)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed("Unexpected master key share count"));
            }

            for (int i = 0; i < shares.Length; i++)
            {
                Result<Unit, SecureStorageFailure> saveResult =
                    await _storage.SaveStateAsync(shares[i], context.ShareStorageKeys[i], context.AccountBytes)
                        .ConfigureAwait(false);

                if (saveResult.IsErr)
                {
                    await CleanupMasterKeySharesAsync(context).ConfigureAwait(false);
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.IdentityStorageFailed(
                            $"Failed to store master key share: {saveResult.UnwrapErr().Message}"));
                }
            }
        }
        finally
        {
            foreach (byte[] share in shares)
            {
                CryptographicOperations.ZeroMemory(share);
            }
        }

        return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>> TryLoadMasterKeyFromSharesAsync(
        IdentityContext context)
    {
        List<byte[]> shares = [];
        byte[]? masterKeyBytes = null;

        try
        {
            foreach (string shareKey in context.ShareStorageKeys)
            {
                Result<byte[], SecureStorageFailure> loadResult =
                    await _storage.LoadStateAsync(shareKey, context.AccountBytes).ConfigureAwait(false);

                if (loadResult.IsErr)
                {
                    if (IsMissingShare(loadResult.UnwrapErr()))
                    {
                        continue;
                    }

                    return Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>.Err(
                        AuthenticationFailure.IdentityStorageFailed(
                            $"Failed to load master key share: {loadResult.UnwrapErr().Message}"));
                }

                shares.Add(loadResult.Unwrap());
            }

            if (shares.Count < MASTER_KEY_SHARE_THRESHOLD)
            {
                return Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>.Ok(
                    Option<SodiumSecureMemoryHandle>.None);
            }

            Result<byte[], EcliptixProtocolFailure> reconstructResult = ShamirSecretSharing.Reconstruct(shares);
            if (reconstructResult.IsErr)
            {
                return Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed(
                        $"Failed to reconstruct master key: {reconstructResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = reconstructResult.Unwrap();
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> handleResult = WriteToSecureMemory(masterKeyBytes);
            if (handleResult.IsErr)
            {
                return Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>.Err(handleResult.UnwrapErr());
            }

            return Result<Option<SodiumSecureMemoryHandle>, AuthenticationFailure>.Ok(
                Option<SodiumSecureMemoryHandle>.Some(handleResult.Unwrap()));
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }

            foreach (byte[] share in shares)
            {
                CryptographicOperations.ZeroMemory(share);
            }
        }
    }

    private async Task CleanupMasterKeySharesAsync(IdentityContext context)
    {
        foreach (string shareKey in context.ShareStorageKeys)
        {
            Result<Unit, SecureStorageFailure> deleteResult =
                await _storage.DeleteStateAsync(shareKey).ConfigureAwait(false);

            if (deleteResult.IsErr)
            {
                Log.Warning("[IDENTITY-SHARE-CLEANUP] Failed to delete master key share: {Error}",
                    deleteResult.UnwrapErr().Message);
            }
        }
    }

    private static bool IsMissingShare(SecureStorageFailure failure) =>
        failure.Message.Contains("state file not found", StringComparison.OrdinalIgnoreCase);

    private static Result<SodiumSecureMemoryHandle, AuthenticationFailure> WriteToSecureMemory(byte[] masterKeyBytes)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
        if (allocResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.SecureMemoryAllocationFailed($"Failed to allocate secure memory: {allocResult.UnwrapErr().Message}"));
        }

        SodiumSecureMemoryHandle handle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = handle.Write(masterKeyBytes);
        if (writeResult.IsErr)
        {
            handle.Dispose();
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.SecureMemoryWriteFailed($"Failed to write to secure memory: {writeResult.UnwrapErr().Message}"));
        }

        return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(handle);
    }

    private sealed class IdentityContext(string accountId)
    {
        public string AccountId { get; } = accountId;
        public string[] ShareStorageKeys { get; } = BuildMasterKeyShareStorageKeys(accountId);
        public byte[] AccountBytes => field ??= Guid.Parse(AccountId).ToByteArray();

        private static string[] BuildMasterKeyShareStorageKeys(string accountId)
        {
            const string prefix = SecureStorageConstants.Identity.MASTER_KEY_SHARE_STORAGE_PREFIX;
            string[] keys = new string[MASTER_KEY_SHARE_COUNT];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = string.Concat(prefix, i + 1, "_", accountId);
            }

            return keys;
        }
    }
}
