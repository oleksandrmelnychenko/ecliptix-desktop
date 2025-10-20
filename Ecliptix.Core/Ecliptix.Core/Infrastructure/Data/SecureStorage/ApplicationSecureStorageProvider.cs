using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Data.SecureStorage.Configuration;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Data.SecureStorage;

internal sealed class ApplicationSecureStorageProvider : IApplicationSecureStorageProvider
{
    private const string SettingsKey = "ApplicationInstanceSettings";

    private readonly IDataProtector _protector;
    private readonly string _storagePath;
    private bool _disposed;

    public ApplicationSecureStorageProvider(
        IOptions<SecureStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        SecureStoreOptions opts = options.Value;

        _storagePath = opts.EncryptedStatePath;
        _protector = dataProtectionProvider.CreateProtector("Ecliptix.SecureStorage.v1");

        InitializeStorageDirectory();
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string? culture)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult = await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Culture = culture;

        return await SecureByteStringInterop.WithByteStringAsSpan(settings.ToByteString(),
            span => StoreAsync(SettingsKey, span.ToArray()));
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationInstanceAsync(bool isNewInstance)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult = await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.IsNewInstance = isNewInstance;
        return await SecureByteStringInterop.WithByteStringAsSpan(settings.ToByteString(),
            span => StoreAsync(SettingsKey, span.ToArray()));
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationIpCountryAsync(IpCountry ipCountry)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult = await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Country = ipCountry.Country;
        settings.IpAddress = ipCountry.IpAddress;

        return await SecureByteStringInterop.WithByteStringAsSpan(settings.ToByteString(),
            span => StoreAsync(SettingsKey, span.ToArray()));
    }

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationMembershipAsync(Membership? membership)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult = await GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsErr)
            return Result<Unit, InternalServiceApiFailure>.Err(settingsResult.UnwrapErr());

        ApplicationInstanceSettings settings = settingsResult.Unwrap();
        settings.Membership = membership;

        return await SecureByteStringInterop.WithByteStringAsSpan(settings.ToByteString(),
            span => StoreAsync(SettingsKey, span.ToArray()));
    }

    public async Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>> GetApplicationInstanceSettingsAsync()
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SettingsKey);
        if (getResult.IsErr)
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(getResult.UnwrapErr());

        Option<byte[]> maybeData = getResult.Unwrap();
        if (!maybeData.HasValue)
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(ApplicationErrorMessages.SecureStorageProvider.ApplicationSettingsNotFound));

        try
        {
            ApplicationInstanceSettings settings = ApplicationInstanceSettings.Parser.ParseFrom(maybeData.Value);
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Ok(settings);
        }
        catch (InvalidProtocolBufferException ex)
        {
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.CorruptSettingsData, ex));
        }
    }

    public async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string? defaultCulture)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SettingsKey);
        if (getResult.IsErr)
        {
            InternalServiceApiFailure failure = getResult.UnwrapErr();
            Log.Warning("[SETTINGS-INIT-RECOVERY] Storage access failed, creating fresh settings. Error: {Error}",
                failure.Message);
            return await CreateAndStoreNewSettingsAsync(defaultCulture);
        }

        Option<byte[]> maybeData = getResult.Unwrap();
        if (maybeData.HasValue)
        {
            try
            {
                ApplicationInstanceSettings settings = ApplicationInstanceSettings.Parser.ParseFrom(maybeData.Value);
                return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
                    new InstanceSettingsResult(settings, false));
            }
            catch (InvalidProtocolBufferException ex)
            {
                Log.Warning("[SETTINGS-INIT-RECOVERY] Settings parsing failed, creating fresh settings. Error: {Error}",
                    ex.Message);
                return await CreateAndStoreNewSettingsAsync(defaultCulture);
            }
        }

        return await CreateAndStoreNewSettingsAsync(defaultCulture);
    }

    private async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> CreateAndStoreNewSettingsAsync(
        string? defaultCulture)
    {
        ApplicationInstanceSettings newSettings = new()
        {
            AppInstanceId = Helpers.GuidToByteString(Guid.NewGuid()),
            DeviceId = Helpers.GuidToByteString(Guid.NewGuid()),
            Culture = defaultCulture
        };

        Result<Unit, InternalServiceApiFailure> storeResult = await SecureByteStringInterop.WithByteStringAsSpan(
            newSettings.ToByteString(),
            span => StoreAsync(SettingsKey, span.ToArray()));

        if (storeResult.IsErr)
        {
            Log.Warning("[SETTINGS-INIT-RECOVERY] Failed to persist fresh settings, continuing in-memory. Error: {Error}",
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
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.FailedToEncryptData, ex));
        }
        catch (IOException ex)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.FailedToWriteToStorage, ex));
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
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.FailedToDecryptData, ex));
        }
        catch (IOException ex)
        {
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.FailedToAccessStorage, ex));
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
                InternalServiceApiFailure.SecureStoreAccessDenied(ApplicationErrorMessages.SecureStorageProvider.FailedToDeleteFromStorage, ex));
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

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(_storagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(string.Format(ApplicationErrorMessages.SecureStorageProvider.SecureStorageDirectoryCreationFailed, _storagePath), ex);
        }
    }

    private void SetSecureFilePermissions(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
