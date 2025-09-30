using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public class ShareAuthenticationService : IShareAuthenticationService
{
    private readonly ISecureProtocolStateStorage _secureStorage;

    public ShareAuthenticationService(ISecureProtocolStateStorage secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public async Task<Result<byte[], string>> GenerateHmacKeyAsync(string identifier)
    {
        try
        {
            byte[] hmacKey = RandomNumberGenerator.GetBytes(64);

            Result<Unit, string> storeResult = await StoreHmacKeyAsync(identifier, hmacKey);
            if (storeResult.IsErr)
            {
                CryptographicOperations.ZeroMemory(hmacKey);
                return Result<byte[], string>.Err($"Failed to store HMAC key: {storeResult.UnwrapErr()}");
            }

            Log.Information("Generated and stored HMAC key for identifier {Identifier}", identifier);
            return Result<byte[], string>.Ok(hmacKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate HMAC key for identifier {Identifier}", identifier);
            return Result<byte[], string>.Err($"Failed to generate HMAC key: {ex.Message}");
        }
    }

    public async Task<Result<Unit, string>> StoreHmacKeyAsync(string identifier, byte[] hmacKey)
    {
        try
        {
            string storageKey = $"hmac_key_{identifier}";
            Result<Unit, SecureStorageFailure> saveResult = await _secureStorage.SaveStateAsync(hmacKey, storageKey);

            if (saveResult.IsErr)
            {
                SecureStorageFailure failure = saveResult.UnwrapErr();
                return Result<Unit, string>.Err($"Failed to store HMAC key: {failure.Message}");
            }

            Log.Debug("Stored HMAC key for identifier {Identifier}", identifier);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store HMAC key for identifier {Identifier}", identifier);
            return Result<Unit, string>.Err($"Failed to store HMAC key: {ex.Message}");
        }
    }

    public async Task<Result<byte[], string>> RetrieveHmacKeyAsync(string identifier)
    {
        try
        {
            string storageKey = $"hmac_key_{identifier}";
            Result<byte[], SecureStorageFailure> loadResult = await _secureStorage.LoadStateAsync(storageKey);

            if (loadResult.IsErr)
            {
                SecureStorageFailure failure = loadResult.UnwrapErr();
                Log.Warning("No HMAC key found for identifier {Identifier}: {Error}", identifier, failure.Message);
                return Result<byte[], string>.Err($"No HMAC key found: {failure.Message}");
            }

            Log.Debug("Retrieved HMAC key for identifier {Identifier}", identifier);
            return Result<byte[], string>.Ok(loadResult.Unwrap());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve HMAC key for identifier {Identifier}", identifier);
            return Result<byte[], string>.Err($"Failed to retrieve HMAC key: {ex.Message}");
        }
    }

    public async Task<Result<bool, string>> HasHmacKeyAsync(string identifier)
    {
        try
        {
            string storageKey = $"hmac_key_{identifier}";
            Result<byte[], SecureStorageFailure> loadResult = await _secureStorage.LoadStateAsync(storageKey);
            return Result<bool, string>.Ok(loadResult.IsOk);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check HMAC key existence for identifier {Identifier}", identifier);
            return Result<bool, string>.Err($"Failed to check HMAC key: {ex.Message}");
        }
    }

    public async Task<Result<Unit, string>> RemoveHmacKeyAsync(string identifier)
    {
        try
        {
            string storageKey = $"hmac_key_{identifier}";
            Result<Unit, SecureStorageFailure> deleteResult = await _secureStorage.DeleteStateAsync(storageKey);

            if (deleteResult.IsErr)
            {
                SecureStorageFailure failure = deleteResult.UnwrapErr();
                Log.Warning("Failed to remove HMAC key for identifier {Identifier}: {Error}", identifier, failure.Message);
                return Result<Unit, string>.Err($"Failed to remove HMAC key: {failure.Message}");
            }

            Log.Debug("Removed HMAC key for identifier {Identifier}", identifier);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove HMAC key for identifier {Identifier}", identifier);
            return Result<Unit, string>.Err($"Failed to remove HMAC key: {ex.Message}");
        }
    }
}