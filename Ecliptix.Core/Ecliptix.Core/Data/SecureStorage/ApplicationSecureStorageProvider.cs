using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Data.SecureStorage.Configuration;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Core;
using Ecliptix.Network.Services.External.IpGeolocation;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Ecliptix.Core.Data.SecureStorage;

internal sealed class ApplicationSecureStorageProvider : IApplicationSecureStorageProvider
{
    private const string SETTINGS_KEY = "ApplicationInstanceSettings";
    private const string WINDOW_PLACEMENT_KEY = "WindowPlacement";

    private readonly IDataProtector _protector;
    private readonly string _storagePath;
    private bool _disposed;

    public ApplicationSecureStorageProvider(
        IOptions<SecureStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        SecureStoreOptions opts = options.Value;

        _storagePath = opts.EncryptedStatePath;
        _protector = dataProtectionProvider.CreateProtector("Ecliptix.SecureStorage.v2");

        InitializeStorageDirectory();
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string? cultureName)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Culture = cultureName ?? settings.Culture;
        return await StoreSettingsAsync(settings);
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationInstanceAsync(bool isNewInstance)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.IsNewInstance = isNewInstance;
        return await StoreSettingsAsync(settings);
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationIpCountryAsync(IpCountry ipCountry)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Country = ipCountry.Country;
        return await StoreSettingsAsync(settings);
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationMembershipAsync(Membership? membership)
    {
        Log.Information("[SECURE-STORAGE] SetApplicationMembershipAsync: Starting, hasMembership={HasMembership}",
            membership != null);

        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();

        if (settingsResult.IsErr)
        {
            Log.Warning("[SECURE-STORAGE] SetApplicationMembershipAsync: Failed to get existing settings: {Error}",
                settingsResult.UnwrapErr().Message);
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        Log.Debug("[SECURE-STORAGE] SetApplicationMembershipAsync: Got existing settings, updating membership");
        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Membership = membership;

        Result<Unit, InternalServiceApiFailure> storeResult = await StoreSettingsAsync(settings);

        if (storeResult.IsOk)
        {
            Log.Information("[SECURE-STORAGE] SetApplicationMembershipAsync: Membership stored successfully");
        }
        else
        {
            Log.Error("[SECURE-STORAGE] SetApplicationMembershipAsync: Failed to store: {Error}",
                storeResult.UnwrapErr().Message);
        }

        return storeResult;
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetCurrentAccountIdAsync(ByteString? accountId)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.CurrentAccountId = accountId ?? ByteString.Empty;
        return await StoreSettingsAsync(settings);
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetWindowPlacementAsync(WindowPlacement windowPlacement)
    {
        try
        {
            byte[] data = SerializeWindowPlacement(windowPlacement);
            return await StoreAsync(WINDOW_PLACEMENT_KEY, data);
        }
        catch (Exception ex)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_WRITE_TO_STORAGE, ex));
        }
    }

    public async Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>>
        GetApplicationInstanceSettingsAsync()
    {
        Log.Debug("[SECURE-STORAGE] GetApplicationInstanceSettingsAsync: Starting");

        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SETTINGS_KEY);
        if (getResult.IsErr)
        {
            Log.Warning("[SECURE-STORAGE] GetApplicationInstanceSettingsAsync: TryGetByKeyAsync failed: {Error}",
                getResult.UnwrapErr().Message);
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        Option<byte[]> maybeData = getResult.Unwrap();
        if (!maybeData.IsSome)
        {
            Log.Debug("[SECURE-STORAGE] GetApplicationInstanceSettingsAsync: No settings found (first run?)");
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(ApplicationErrorMessages.SecureStorageProvider
                    .APPLICATION_SETTINGS_NOT_FOUND));
        }

        try
        {
            if (maybeData.Value == null || maybeData.Value.Length == 0)
            {
                Log.Warning("[SECURE-STORAGE] GetApplicationInstanceSettingsAsync: Data is null or empty");
                return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.SecureStoreAccessDenied(
                        ApplicationErrorMessages.SecureStorageProvider.CORRUPT_SETTINGS_DATA));
            }

            ApplicationInstanceSettings settings = DeserializeSettings(maybeData.Value);
            Log.Information("[SECURE-STORAGE] GetApplicationInstanceSettingsAsync: Settings loaded successfully, hasMembership={HasMembership}",
                settings.Membership != null);
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Ok(settings);
        }
        catch (Exception ex)
        {
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.CORRUPT_SETTINGS_DATA, ex));
        }
    }

    public async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string? defaultCulture)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SETTINGS_KEY);
        if (getResult.IsErr)
        {
            InternalServiceApiFailure failure = getResult.UnwrapErr();
            Log.Warning("[SETTINGS-INIT-RECOVERY] Storage access failed, creating fresh settings. ERROR: {Error}",
                failure.Message);
            return await CreateAndStoreNewSettingsAsync(defaultCulture);
        }

        Option<byte[]> maybeData = getResult.Unwrap();
        if (!maybeData.IsSome)
        {
            return await CreateAndStoreNewSettingsAsync(defaultCulture);
        }

        try
        {
            if (maybeData.Value == null || maybeData.Value.Length == 0)
            {
                return await CreateAndStoreNewSettingsAsync(defaultCulture);
            }

            ApplicationInstanceSettings settings = DeserializeSettings(maybeData.Value);
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
                new InstanceSettingsResult(settings, false));
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "[SETTINGS-INIT-RECOVERY] Settings parsing failed, creating fresh settings. ERROR: {Error}",
                ex.Message);
            return await CreateAndStoreNewSettingsAsync(defaultCulture);
        }
    }

    private async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> CreateAndStoreNewSettingsAsync(
        string? defaultCulture)
    {
        ApplicationInstanceSettings newSettings = new()
        {
            AppInstanceId = Helpers.GuidToByteString(Guid.NewGuid()),
            DeviceId = Helpers.GuidToByteString(Guid.NewGuid()),
            Country = "Unknown",
            Culture = defaultCulture ?? "en-US",
            CurrentAccountId = ByteString.Empty,
            IsNewInstance = true
        };

        Result<Unit, InternalServiceApiFailure> storeResult = await StoreSettingsAsync(newSettings);
        if (storeResult.IsErr)
        {
            Log.Warning(
                "[SETTINGS-INIT-RECOVERY] Failed to persist fresh settings, continuing in-memory. ERROR: {Error}",
                storeResult.UnwrapErr().Message);
        }

        return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
            new InstanceSettingsResult(newSettings, true));
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data)
    {
        string filePath = GetHashedFilePath(key);
        Log.Debug("[SECURE-STORAGE] StoreAsync: Starting write for key={Key}, dataLength={Length}, filePath={Path}",
            key, data.Length, filePath);

        try
        {
            byte[] protectedData = _protector.Protect(data);
            Log.Debug("[SECURE-STORAGE] StoreAsync: Data protected, protectedLength={Length}", protectedData.Length);

            await File.WriteAllBytesAsync(filePath, protectedData);
            Log.Debug("[SECURE-STORAGE] StoreAsync: File written successfully");

            SetSecureFilePermissions(filePath);

            // Verify the write
            FileInfo fileInfo = new(filePath);
            Log.Information("[SECURE-STORAGE] StoreAsync: Write complete for key={Key}, fileSize={Size}, lastWrite={LastWrite}",
                key, fileInfo.Length, fileInfo.LastWriteTime);

            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "[SECURE-STORAGE] StoreAsync: Encryption failed for key={Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_ENCRYPT_DATA, ex));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "[SECURE-STORAGE] StoreAsync: IO error writing key={Key}, path={Path}", key, filePath);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_WRITE_TO_STORAGE, ex));
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        string filePath = GetHashedFilePath(key);
        Log.Debug("[SECURE-STORAGE] TryGetByKeyAsync: Starting read for key={Key}, filePath={Path}", key, filePath);

        if (!File.Exists(filePath))
        {
            Log.Debug("[SECURE-STORAGE] TryGetByKeyAsync: File does not exist for key={Key}", key);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
        }

        try
        {
            FileInfo fileInfo = new(filePath);
            Log.Debug("[SECURE-STORAGE] TryGetByKeyAsync: File exists, size={Size}, lastWrite={LastWrite}",
                fileInfo.Length, fileInfo.LastWriteTime);

            byte[] protectedData = await File.ReadAllBytesAsync(filePath);
            Log.Debug("[SECURE-STORAGE] TryGetByKeyAsync: Read {Length} bytes from file", protectedData.Length);

            if (protectedData.Length == 0)
            {
                Log.Warning("[SECURE-STORAGE] TryGetByKeyAsync: File is empty for key={Key}", key);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            byte[] data = _protector.Unprotect(protectedData);
            Log.Information("[SECURE-STORAGE] TryGetByKeyAsync: Successfully decrypted key={Key}, decryptedLength={Length}",
                key, data.Length);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "[SECURE-STORAGE] TryGetByKeyAsync: Decryption failed for key={Key}, path={Path}. This may indicate key rotation or corrupted data.",
                key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_DECRYPT_DATA, ex));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "[SECURE-STORAGE] TryGetByKeyAsync: IO error reading key={Key}, path={Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_ACCESS_STORAGE, ex));
        }
    }

    public Result<Unit, InternalServiceApiFailure> Delete(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        try
        {
            string filePath = GetHashedFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (IOException ex)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_DELETE_FROM_STORAGE, ex));
        }
    }

    private string GetHashedFilePath(string key)
    {
        int byteCount = Encoding.UTF8.GetByteCount(key);
        Span<byte> keyBytes = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(key, keyBytes);

        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(keyBytes, hashBytes);
        return Path.Combine(_storagePath, $"{Convert.ToHexString(hashBytes)}.enc");
    }

    private void InitializeStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(_storagePath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                string.Format(ApplicationErrorMessages.SecureStorageProvider.SECURE_STORAGE_DIRECTORY_CREATION_FAILED,
                    _storagePath), ex);
        }
    }

    private static void SetSecureFilePermissions(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    private async Task<Result<Unit, InternalServiceApiFailure>> StoreSettingsAsync(ApplicationInstanceSettings settings)
    {
        try
        {
            byte[] data = SerializeSettings(settings);
            return await StoreAsync(SETTINGS_KEY, data);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SECURE-STORAGE] Failed to store application settings. ERROR: {Error}", ex.Message);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_WRITE_TO_STORAGE, ex));
        }
    }

    private static byte[] SerializeSettings(ApplicationInstanceSettings settings)
    {
        ApplicationInstanceSettings proto = settings.Clone();
        proto.AppInstanceId = proto.AppInstanceId ?? ByteString.Empty;
        proto.DeviceId = proto.DeviceId ?? ByteString.Empty;
        proto.Country ??= string.Empty;
        proto.Culture ??= string.Empty;
        proto.CurrentAccountId = proto.CurrentAccountId ?? ByteString.Empty;

        if (proto.WindowPlacement is { } placement)
        {
            proto.WindowPlacement = MapWindowPlacementToProto(placement);
        }

        return proto.ToByteArray();
    }

    private static ApplicationInstanceSettings DeserializeSettings(byte[] payload)
    {
        ApplicationInstanceSettings settings = ApplicationInstanceSettings.Parser.ParseFrom(payload);

        if (settings.WindowPlacement != null)
        {
            settings.WindowPlacement = MapWindowPlacementFromProto(settings.WindowPlacement);
        }

        return settings;
    }

    private static byte[] SerializeWindowPlacement(WindowPlacement placement) =>
        MapWindowPlacementToProto(placement).ToByteArray();

    private static WindowPlacement MapWindowPlacementToProto(WindowPlacement placement) =>
        placement.Clone();

    private static WindowPlacement MapWindowPlacementFromProto(WindowPlacement proto)
    {
        WindowPlacement placement = proto.Clone();
        placement.IsValidSave = placement.ClientWidth > 0 && placement.ClientHeight > 0;
        return placement;
    }
}
