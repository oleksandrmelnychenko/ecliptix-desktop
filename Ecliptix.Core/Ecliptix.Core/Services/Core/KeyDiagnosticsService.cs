using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.KeySplitting;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public sealed class KeyDiagnosticsService : IKeyDiagnosticsService
{
    private readonly IPlatformSecurityProvider _platformSecurityProvider;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IHmacKeyManager _hmacKeyManager;

    public KeyDiagnosticsService(
        IPlatformSecurityProvider platformSecurityProvider,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IHmacKeyManager hmacKeyManager)
    {
        _platformSecurityProvider = platformSecurityProvider;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _hmacKeyManager = hmacKeyManager;
    }

    public async Task<KeyStorageDiagnostics> DiagnoseKeyStorageAsync(string membershipId)
    {
        Guid membershipGuid = Guid.Parse(membershipId);
        string identifier = membershipId;

        Log.Information("[KEY-DIAGNOSTICS] ========== KEY STORAGE DIAGNOSTICS STARTED ==========");
        Log.Information("[KEY-DIAGNOSTICS] Running diagnostics for MembershipId: {MembershipId}", membershipId);

        KeyStorageDiagnostics diagnostics = new()
        {
            MembershipId = membershipId,
            DiagnosticTimestamp = DateTime.UtcNow
        };

        diagnostics.HardwareSecurityAvailable = _platformSecurityProvider.IsHardwareSecurityAvailable();
        Log.Information("[KEY-DIAGNOSTICS] Hardware security available: {Available}", diagnostics.HardwareSecurityAvailable);

        await DiagnoseShareLocationsAsync(diagnostics, identifier);

        await DiagnoseHmacKeyAsync(diagnostics, membershipId);

        await DiagnoseLocalStorageAsync(diagnostics, identifier);

        Log.Information("[KEY-DIAGNOSTICS] ========== KEY STORAGE DIAGNOSTICS COMPLETED ==========");
        Log.Information("[KEY-DIAGNOSTICS] Summary: TotalShares={Total}, Available={Available}, Missing={Missing}, Corrupted={Corrupted}, HmacAvailable={HmacAvailable}",
            diagnostics.ShareLocations.Count,
            diagnostics.ShareLocations.Count(s => s.IsAvailable),
            diagnostics.ShareLocations.Count(s => !s.IsAvailable && !s.IsCorrupted),
            diagnostics.ShareLocations.Count(s => s.IsCorrupted),
            diagnostics.HmacKeyAvailable);

        return diagnostics;
    }

    private async Task DiagnoseShareLocationsAsync(KeyStorageDiagnostics diagnostics, string identifier)
    {
        for (int shareIndex = 0; shareIndex < 5; shareIndex++)
        {
            ShareLocationDiagnostics locationDiag = await DiagnoseShareLocationAsync(shareIndex, identifier);
            diagnostics.ShareLocations.Add(locationDiag);
        }
    }

    private async Task<ShareLocationDiagnostics> DiagnoseShareLocationAsync(int shareIndex, string identifier)
    {
        string locationName = GetShareLocationName(shareIndex);
        string shareKey = $"{GetSharePrefix(shareIndex)}_share_{identifier}_{shareIndex}";

        ShareLocationDiagnostics diag = new()
        {
            ShareIndex = shareIndex,
            LocationName = locationName,
            ShareKey = shareKey
        };

        Log.Information("[KEY-DIAGNOSTICS-SHARE] Diagnosing share location {Index}: {Location}, ShareKey: {ShareKey}",
            shareIndex, locationName, shareKey);

        try
        {
            byte[]? shareData = shareIndex switch
            {
                0 => await DiagnoseHardwareShareAsync(diag, shareKey),
                1 or 2 => await DiagnoseKeychainShareAsync(diag, shareKey),
                3 => await DiagnoseLocalEncryptedShareAsync(diag, identifier),
                4 => await DiagnoseBackupShareAsync(diag, shareKey),
                _ => null
            };

            if (shareData != null)
            {
                diag.IsAvailable = true;
                diag.DataSize = shareData.Length;
                CryptographicOperations.ZeroMemory(shareData);

                Log.Information("[KEY-DIAGNOSTICS-SHARE] Share {Index} ({Location}) is AVAILABLE. Size: {Size} bytes",
                    shareIndex, locationName, diag.DataSize);
            }
            else
            {
                diag.IsAvailable = false;
                Log.Warning("[KEY-DIAGNOSTICS-SHARE] Share {Index} ({Location}) is MISSING",
                    shareIndex, locationName);
            }
        }
        catch (Exception ex)
        {
            diag.IsAvailable = false;
            diag.IsCorrupted = true;
            diag.ErrorMessage = ex.Message;

            Log.Error("[KEY-DIAGNOSTICS-SHARE-ERROR] Share {Index} ({Location}) check failed. Error: {Error}",
                shareIndex, locationName, ex.Message);
        }

        return diag;
    }

    private async Task<byte[]?> DiagnoseHardwareShareAsync(ShareLocationDiagnostics diag, string shareKey)
    {
        byte[]? hwShare = await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);
        if (hwShare == null) return null;

        diag.IsEncrypted = _platformSecurityProvider.IsHardwareSecurityAvailable();

        if (diag.IsEncrypted)
        {
            try
            {
                byte[] decryptedShare = await _platformSecurityProvider.HardwareDecryptAsync(hwShare);
                CryptographicOperations.ZeroMemory(hwShare);
                return decryptedShare;
            }
            catch (Exception ex)
            {
                CryptographicOperations.ZeroMemory(hwShare);
                diag.IsCorrupted = true;
                diag.ErrorMessage = $"Hardware decryption failed: {ex.Message}";
                return null;
            }
        }

        return hwShare;
    }

    private async Task<byte[]?> DiagnoseKeychainShareAsync(ShareLocationDiagnostics diag, string shareKey) =>
        await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);

    private async Task<byte[]?> DiagnoseBackupShareAsync(ShareLocationDiagnostics diag, string shareKey) =>
        await _platformSecurityProvider.GetKeyFromKeychainAsync($"backup_{shareKey}");

    private async Task<byte[]?> DiagnoseLocalEncryptedShareAsync(ShareLocationDiagnostics diag, string identifier)
    {
        string localShareKey = $"{StorageKeyConstants.Share.LocalPrefix}{identifier}";
        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await _applicationSecureStorageProvider.TryGetByKeyAsync(localShareKey);

        if (getResult.IsErr)
        {
            diag.ErrorMessage = getResult.UnwrapErr().Message;
            return null;
        }

        if (!getResult.Unwrap().HasValue) return null;

        byte[]? encryptedData = getResult.Unwrap().Value;
        if (encryptedData == null) return null;

        diag.IsEncrypted = true;
        diag.DataSize = encryptedData.Length;

        return encryptedData;
    }

    private async Task DiagnoseHmacKeyAsync(KeyStorageDiagnostics diagnostics, string membershipId)
    {
        Log.Information("[KEY-DIAGNOSTICS-HMAC] Checking HMAC key availability for MembershipId: {MembershipId}",
            membershipId);

        try
        {
            Result<SodiumSecureMemoryHandle, KeySplittingFailure> hmacKeyResult =
                await _hmacKeyManager.RetrieveHmacKeyHandleAsync(membershipId);

            if (hmacKeyResult.IsOk)
            {
                using SodiumSecureMemoryHandle hmacKey = hmacKeyResult.Unwrap();
                diagnostics.HmacKeyAvailable = true;
                diagnostics.HmacKeySize = hmacKey.Length;

                Log.Information("[KEY-DIAGNOSTICS-HMAC] HMAC key is AVAILABLE. Size: {Size} bytes",
                    hmacKey.Length);
            }
            else
            {
                diagnostics.HmacKeyAvailable = false;
                diagnostics.HmacKeyError = hmacKeyResult.UnwrapErr().Message;

                Log.Warning("[KEY-DIAGNOSTICS-HMAC] HMAC key is MISSING. Error: {Error}",
                    diagnostics.HmacKeyError);
            }
        }
        catch (Exception ex)
        {
            diagnostics.HmacKeyAvailable = false;
            diagnostics.HmacKeyError = ex.Message;

            Log.Error("[KEY-DIAGNOSTICS-HMAC-ERROR] HMAC key check failed. Error: {Error}",
                ex.Message);
        }
    }

    private async Task DiagnoseLocalStorageAsync(KeyStorageDiagnostics diagnostics, string identifier)
    {
        Log.Information("[KEY-DIAGNOSTICS-STORAGE] Checking local storage for MembershipId: {MembershipId}",
            identifier);

        try
        {
            string localShareKey = $"{StorageKeyConstants.Share.LocalPrefix}{identifier}";
            Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                await _applicationSecureStorageProvider.TryGetByKeyAsync(localShareKey);

            if (getResult.IsOk && getResult.Unwrap().HasValue)
            {
                byte[]? data = getResult.Unwrap().Value;
                diagnostics.LocalStorageAvailable = data != null;
                diagnostics.LocalStorageSize = data?.Length ?? 0;

                Log.Information("[KEY-DIAGNOSTICS-STORAGE] Local storage is AVAILABLE. Size: {Size} bytes",
                    diagnostics.LocalStorageSize);
            }
            else
            {
                diagnostics.LocalStorageAvailable = false;
                Log.Warning("[KEY-DIAGNOSTICS-STORAGE] Local storage is MISSING or EMPTY");
            }
        }
        catch (Exception ex)
        {
            diagnostics.LocalStorageAvailable = false;
            diagnostics.LocalStorageError = ex.Message;

            Log.Error("[KEY-DIAGNOSTICS-STORAGE-ERROR] Local storage check failed. Error: {Error}",
                ex.Message);
        }
    }

    private static string GetShareLocationName(int shareIndex) => shareIndex switch
    {
        0 => "Hardware/Keychain",
        1 => "Keychain-1",
        2 => "Keychain-2",
        3 => "Local-Encrypted-File",
        4 => "Backup-Keychain",
        _ => "Unknown"
    };

    private static string GetSharePrefix(int shareIndex) => shareIndex switch
    {
        0 => StorageKeyConstants.Share.HardwarePrefix[..^1],
        1 => StorageKeyConstants.Share.KeychainPrefix[..^1],
        2 => StorageKeyConstants.Share.MemoryPrefix,
        3 => StorageKeyConstants.Share.LocalPrefix[..^1],
        4 => StorageKeyConstants.Share.BackupPrefix[..^1],
        _ => "unknown"
    };
}

public sealed class KeyStorageDiagnostics
{
    public string MembershipId { get; set; } = string.Empty;
    public DateTime DiagnosticTimestamp { get; set; }
    public bool HardwareSecurityAvailable { get; set; }
    public List<ShareLocationDiagnostics> ShareLocations { get; set; } = new();
    public bool HmacKeyAvailable { get; set; }
    public int HmacKeySize { get; set; }
    public string? HmacKeyError { get; set; }
    public bool LocalStorageAvailable { get; set; }
    public int LocalStorageSize { get; set; }
    public string? LocalStorageError { get; set; }
}

public sealed class ShareLocationDiagnostics
{
    public int ShareIndex { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string ShareKey { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsCorrupted { get; set; }
    public int DataSize { get; set; }
    public string? ErrorMessage { get; set; }
}
