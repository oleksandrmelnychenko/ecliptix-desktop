using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.IpGeolocation;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Ecliptix.Core.Persistors;

public sealed class ApplicationSecureStorageProvider : IApplicationSecureStorageProvider
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

    public async Task<Result<Unit, InternalServiceApiFailure>> SetApplicationMembershipAsync(Membership membership)
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
                InternalServiceApiFailure.SecureStoreKeyNotFound("Application instance settings not found."));

        try
        {
            ApplicationInstanceSettings settings = ApplicationInstanceSettings.Parser.ParseFrom(maybeData.Value);
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Ok(settings);
        }
        catch (InvalidProtocolBufferException ex)
        {
            Log.Error(ex, "Failed to parse application instance settings");
            return Result<ApplicationInstanceSettings, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Corrupt settings data in secure storage.", ex));
        }
    }

    public async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string? defaultCulture)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> getResult = await TryGetByKeyAsync(SettingsKey);
        if (getResult.IsErr)
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Err(getResult.UnwrapErr());

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
                Log.Error(ex, "Corrupt protobuf data during initialization");
                return Result<InstanceSettingsResult, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.SecureStoreAccessDenied("Corrupt settings data in secure storage.", ex));
            }
        }

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
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Err(storeResult.UnwrapErr());

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
            Log.Debug("Stored data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "Failed to encrypt data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to encrypt data for storage.", ex));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to write data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to write to secure storage.", ex));
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        string filePath = GetHashedFilePath(key);
        if (!File.Exists(filePath))
        {
            Log.Debug("No data found for key {Key} at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
        }

        try
        {
            byte[] protectedData = await File.ReadAllBytesAsync(filePath);
            if (protectedData.Length == 0)
            {
                Log.Warning("Empty file for key {Key} at {Path}", key, filePath);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            byte[] data = _protector.Unprotect(protectedData);
            Log.Debug("Retrieved data for key {Key}", key);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "Failed to decrypt data for key {Key}", key);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to decrypt data.", ex));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to read file for key {Key} at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to access secure storage.", ex));
        }
    }

    public Result<Unit, InternalServiceApiFailure> DeleteAsync(string key)
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
                Log.Debug("Deleted data for key {Key}", filePath);
            }
            else
            {
                Log.Debug("No data to delete for key {Key}", key);
            }
            return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to delete data for key {Key}", key);
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to delete from secure storage.", ex));
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
                    File.SetUnixFileMode(_storagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            Log.Debug("Initialized secure storage at {Path}", _storagePath);
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to initialize secure storage directory at {Path}", _storagePath);
            throw new InvalidOperationException($"Could not create secure storage directory: {_storagePath}", ex);
        }
    }

    private void SetSecureFilePermissions(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Log.Debug("Set permissions on {Path}", filePath);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Failed to set permissions for {Path}", filePath);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        return ValueTask.CompletedTask;
    }
}