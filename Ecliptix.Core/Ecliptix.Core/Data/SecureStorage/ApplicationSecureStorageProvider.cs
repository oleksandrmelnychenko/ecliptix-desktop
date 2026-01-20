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
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Membership = membership;
        return await StoreSettingsAsync(settings);
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
        settings.CurrentAccountId = accountId;
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
        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SETTINGS_KEY);
        if (getResult.IsErr)
        {
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        Option<byte[]> maybeData = getResult.Unwrap();
        if (!maybeData.IsSome)
        {
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(ApplicationErrorMessages.SecureStorageProvider
                    .APPLICATION_SETTINGS_NOT_FOUND));
        }

        try
        {
            if (maybeData.Value == null || maybeData.Value.Length == 0)
            {
                return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.SecureStoreAccessDenied(
                        ApplicationErrorMessages.SecureStorageProvider.CORRUPT_SETTINGS_DATA));
            }

            ApplicationInstanceSettings settings = DeserializeSettings(maybeData.Value);
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
        try
        {
            string filePath = GetHashedFilePath(key);
            byte[] protectedData = _protector.Protect(data);
            await File.WriteAllBytesAsync(filePath, protectedData);
            SetSecureFilePermissions(filePath);
            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (CryptographicException ex)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_ENCRYPT_DATA, ex));
        }
        catch (IOException ex)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_WRITE_TO_STORAGE, ex));
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        string filePath = GetHashedFilePath(key);
        if (!File.Exists(filePath))
        {
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
        }

        try
        {
            byte[] protectedData = await File.ReadAllBytesAsync(filePath);
            if (protectedData.Length == 0)
            {
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            byte[] data = _protector.Unprotect(protectedData);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(
                    ApplicationErrorMessages.SecureStorageProvider.FAILED_TO_DECRYPT_DATA, ex));
        }
        catch (IOException ex)
        {
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
