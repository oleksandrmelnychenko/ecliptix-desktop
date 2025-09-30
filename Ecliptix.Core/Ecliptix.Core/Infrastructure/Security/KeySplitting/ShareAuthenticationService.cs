using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public class ShareAuthenticationService(ISecureProtocolStateStorage secureStorage) : IShareAuthenticationService
{
    private const int HmacKeySizeBytes = 64;

    private static Result<Unit, KeySplittingFailure> ValidateIdentifier(string identifier)
    {
        if (Guid.TryParse(identifier, out _))
            return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);

        if (uint.TryParse(identifier, out _))
            return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);

        return Result<Unit, KeySplittingFailure>.Err(
            KeySplittingFailure.InvalidIdentifier($"Identifier must be a valid GUID or numeric value: {identifier}"));
    }

    private async Task<Result<byte[], KeySplittingFailure>> GenerateHmacKeyAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(validationResult.UnwrapErr());

        Result<bool, KeySplittingFailure> hasKeyResult = await HasHmacKeyAsync(identifier);
        if (hasKeyResult.IsOk && hasKeyResult.Unwrap())
        {
            return await RetrieveHmacKeyAsync(identifier);
        }

        byte[] hmacKey = RandomNumberGenerator.GetBytes(HmacKeySizeBytes);

        Result<Unit, KeySplittingFailure> storeResult = await StoreHmacKeyAsync(identifier, hmacKey);
        if (storeResult.IsErr)
        {
            CryptographicOperations.ZeroMemory(hmacKey);
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.HmacKeyStorageFailed(storeResult.UnwrapErr().ToString()));
        }

        return Result<byte[], KeySplittingFailure>.Ok(hmacKey);
    }

    private async Task<Result<Unit, KeySplittingFailure>> StoreHmacKeyAsync(string identifier, byte[] hmacKey)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return validationResult;

        string storageKey = $"hmac_key_{identifier}";
        Result<Unit, SecureStorageFailure> saveResult = await secureStorage.SaveStateAsync(hmacKey, storageKey);

        if (saveResult.IsErr)
        {
            SecureStorageFailure failure = saveResult.UnwrapErr();
            return Result<Unit, KeySplittingFailure>.Err(KeySplittingFailure.HmacKeyStorageFailed(failure.Message));
        }

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }

    private async Task<Result<byte[], KeySplittingFailure>> RetrieveHmacKeyAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(validationResult.UnwrapErr());

        string storageKey = $"hmac_key_{identifier}";
        Result<byte[], SecureStorageFailure> loadResult = await secureStorage.LoadStateAsync(storageKey);

        if (loadResult.IsErr)
        {
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.HmacKeyMissing(identifier));
        }

        return Result<byte[], KeySplittingFailure>.Ok(loadResult.Unwrap());
    }

    private async Task<Result<bool, KeySplittingFailure>> HasHmacKeyAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return Result<bool, KeySplittingFailure>.Err(validationResult.UnwrapErr());

        string storageKey = $"hmac_key_{identifier}";
        Result<byte[], SecureStorageFailure> loadResult = await secureStorage.LoadStateAsync(storageKey);
        return Result<bool, KeySplittingFailure>.Ok(loadResult.IsOk);
    }

    public async Task<Result<Unit, KeySplittingFailure>> RemoveHmacKeyAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return validationResult;

        string storageKey = $"hmac_key_{identifier}";
        Result<Unit, SecureStorageFailure> deleteResult = await secureStorage.DeleteStateAsync(storageKey);

        if (deleteResult.IsErr)
        {
            SecureStorageFailure failure = deleteResult.UnwrapErr();
            return Result<Unit, KeySplittingFailure>.Err(KeySplittingFailure.HmacKeyRemovalFailed(failure.Message));
        }

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }

    public async Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> GenerateHmacKeyHandleAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(validationResult.UnwrapErr());

        Result<byte[], KeySplittingFailure> generateResult = await GenerateHmacKeyAsync(identifier);
        if (generateResult.IsErr)
            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(generateResult.UnwrapErr());

        byte[] hmacKey = generateResult.Unwrap();

        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(hmacKey.Length);
            if (allocateResult.IsErr)
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(KeySplittingFailure.AllocationFailed(allocateResult.UnwrapErr().Message));

            SodiumSecureMemoryHandle handle = allocateResult.Unwrap();

            Result<Unit, SodiumFailure> writeResult = handle.Write(hmacKey);
            if (writeResult.IsErr)
            {
                handle.Dispose();
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(KeySplittingFailure.MemoryWriteFailed(writeResult.UnwrapErr().Message));
            }

            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Ok(handle);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> RetrieveHmacKeyHandleAsync(string identifier)
    {
        Result<Unit, KeySplittingFailure> validationResult = ValidateIdentifier(identifier);
        if (validationResult.IsErr)
            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(validationResult.UnwrapErr());

        Result<byte[], KeySplittingFailure> retrieveResult = await RetrieveHmacKeyAsync(identifier);
        if (retrieveResult.IsErr)
            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(retrieveResult.UnwrapErr());

        byte[] hmacKey = retrieveResult.Unwrap();

        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(hmacKey.Length);
            if (allocateResult.IsErr)
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(KeySplittingFailure.AllocationFailed(allocateResult.UnwrapErr().Message));

            SodiumSecureMemoryHandle handle = allocateResult.Unwrap();

            Result<Unit, SodiumFailure> writeResult = handle.Write(hmacKey);
            if (writeResult.IsErr)
            {
                handle.Dispose();
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(KeySplittingFailure.MemoryWriteFailed(writeResult.UnwrapErr().Message));
            }

            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Ok(handle);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }
}