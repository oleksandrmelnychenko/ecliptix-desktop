using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Ecliptix.Core.Persistors;

public sealed class SecureStorageProvider : ISecureStorageProvider
{
    private const string SettingsKey = "ApplicationInstanceSettings";

    private readonly IDataProtector _protector;
    private readonly string _storagePath;

    private bool _disposed;

    public SecureStorageProvider(
        IOptions<SecureStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        SecureStoreOptions opts = options.Value;

        _storagePath = opts.EncryptedStatePath;
        _protector = dataProtectionProvider.CreateProtector("Ecliptix.SecureStorage.v1");

        InitializeStorageDirectory();
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string culture)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await TryGetByKeyAsync(SettingsKey);

        if (getResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        ApplicationInstanceSettings existingSettings =
            ApplicationInstanceSettings.Parser.ParseFrom(getResult.Unwrap().Value);
        existingSettings.Culture = culture;

        await StoreAsync(SettingsKey, existingSettings.ToByteArray());
        return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
    }

    public async Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>>
        GetApplicationInstanceSettingsAsync()
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await TryGetByKeyAsync(SettingsKey);

        if (getResult.IsErr)
        {
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        Option<byte[]> maybeSettingsData = getResult.Unwrap();

        if (maybeSettingsData.HasValue)
        {
            ApplicationInstanceSettings? existingSettings =
                ApplicationInstanceSettings.Parser.ParseFrom(maybeSettingsData.Value);
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Ok(existingSettings);
        }

        return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
            InternalServiceApiFailure.SecureStoreKeyNotFound("Application instance settings not found."));
    }

    public async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string defaultCulture)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await TryGetByKeyAsync(SettingsKey);

        if (getResult.IsErr)
        {
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        Option<byte[]> maybeSettingsData = getResult.Unwrap();

        if (maybeSettingsData.HasValue)
        {
            ApplicationInstanceSettings? existingSettings =
                ApplicationInstanceSettings.Parser.ParseFrom(maybeSettingsData.Value);
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
                new InstanceSettingsResult(existingSettings, false));
        }

        ApplicationInstanceSettings newSettings = new()
        {
            AppInstanceId = Helpers.GuidToByteString(Guid.NewGuid()),
            DeviceId = Helpers.GuidToByteString(Guid.NewGuid()),
            Culture = defaultCulture
        };

        await StoreAsync(SettingsKey, newSettings.ToByteArray());
        return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
            new InstanceSettingsResult(newSettings, true));
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data)
    {
        try
        {
            string filePath = GetHashedFilePath(key);
            byte[] protectedData = _protector.Protect(data);

            await File.WriteAllBytesAsync(filePath, protectedData).ConfigureAwait(false);
            SetSecureFilePermissions(filePath);

            Log.Debug("Successfully stored data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to write to secure storage.", ex));
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        string filePath = GetHashedFilePath(key);

        if (!File.Exists(filePath))
        {
            Log.Debug("No data found for key {Key}, as file does not exist at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
        }

        try
        {
            byte[] protectedData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            if (protectedData.Length == 0)
            {
                Log.Warning("File for key {Key} at {Path} is empty. Treating as non-existent", key, filePath);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            byte[] data = _protector.Unprotect(protectedData);
            Log.Debug("Successfully retrieved and decrypted data for key {Key}", key);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex,
                "Failed to decrypt data for key {Key}. The data might be corrupt or the protection keys have changed",
                key);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied($"Failed to decrypt data: {ex.Message}"));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to read file for key {Key} at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to access secure storage.", ex));
        }
    }

    public Task<Result<Unit, InternalServiceApiFailure>> DeleteAsync(string key)
    {
        try
        {
            string filePath = GetHashedFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Debug("Successfully deleted data for key {Key}", key);
            }
            else
            {
                Log.Debug("No data to delete for key {Key} as file does not exist", key);
            }

            return Task.FromResult(Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete data for key {Key}", key);
            return Task.FromResult(Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to delete from secure storage.", ex)));
        }
    }

    private string GetHashedFilePath(string key)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string safeFilename = Convert.ToHexString(hashBytes);
        return Path.Combine(_storagePath, $"{safeFilename}.enc");
    }

    private void InitializeStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
                Log.Information("Created secure storage directory: {Path}", _storagePath);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(_storagePath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 700
                }
            }

            Log.Debug("SecureStorageProvider initialized with path {Path}", _storagePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize secure storage directory at {Path}", _storagePath);
            throw new InvalidOperationException(
                $"Could not create or access the secure storage directory: {_storagePath}", ex);
        }
    }

    private void SetSecureFilePermissions(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 600
                Log.Debug("Set secure file permissions on {Path}", filePath);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Failed to set secure permissions for file {Path}", filePath);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
            await Task.CompletedTask;
        }
    }
}