using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
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

    public KeyDiagnosticsService(
        IPlatformSecurityProvider platformSecurityProvider,
        IApplicationSecureStorageProvider applicationSecureStorageProvider)
    {
        _platformSecurityProvider = platformSecurityProvider;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
    }

    public async Task<KeyStorageDiagnostics> DiagnoseKeyStorageAsync(string membershipId)
    {
        Log.Information("[KEY-DIAGNOSTICS] ========== KEY STORAGE DIAGNOSTICS STARTED ==========");
        Log.Information("[KEY-DIAGNOSTICS] Running diagnostics for MembershipId: {MembershipId}", membershipId);

        KeyStorageDiagnostics diagnostics = new()
        {
            MembershipId = membershipId,
            DiagnosticTimestamp = DateTime.UtcNow
        };

        diagnostics.HardwareSecurityAvailable = _platformSecurityProvider.IsHardwareSecurityAvailable();
        Log.Information("[KEY-DIAGNOSTICS] Hardware security available: {Available}", diagnostics.HardwareSecurityAvailable);

        await DiagnoseLocalStorageAsync(diagnostics, membershipId);

        Log.Information("[KEY-DIAGNOSTICS] ========== KEY STORAGE DIAGNOSTICS COMPLETED ==========");
        Log.Information("[KEY-DIAGNOSTICS] Summary: LocalStorageAvailable={Available}",
            diagnostics.LocalStorageAvailable);

        return diagnostics;
    }

    private async Task DiagnoseLocalStorageAsync(KeyStorageDiagnostics diagnostics, string membershipId)
    {
        Log.Information("[KEY-DIAGNOSTICS-STORAGE] Checking master key storage for MembershipId: {MembershipId}",
            membershipId);

        try
        {
            string storageKey = $"master_{membershipId}";
            Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                await _applicationSecureStorageProvider.TryGetByKeyAsync(storageKey);

            if (getResult.IsOk && getResult.Unwrap().HasValue)
            {
                byte[]? data = getResult.Unwrap().Value;
                diagnostics.LocalStorageAvailable = data != null;
                diagnostics.LocalStorageSize = data?.Length ?? 0;

                Log.Information("[KEY-DIAGNOSTICS-STORAGE] Master key storage is AVAILABLE. Size: {Size} bytes",
                    diagnostics.LocalStorageSize);
            }
            else
            {
                diagnostics.LocalStorageAvailable = false;
                Log.Warning("[KEY-DIAGNOSTICS-STORAGE] Master key storage is MISSING or EMPTY");
            }
        }
        catch (Exception ex)
        {
            diagnostics.LocalStorageAvailable = false;
            diagnostics.LocalStorageError = ex.Message;

            Log.Error("[KEY-DIAGNOSTICS-STORAGE-ERROR] Master key storage check failed. Error: {Error}",
                ex.Message);
        }
    }
}

public sealed class KeyStorageDiagnostics
{
    public string MembershipId { get; set; } = string.Empty;
    public DateTime DiagnosticTimestamp { get; set; }
    public bool HardwareSecurityAvailable { get; set; }
    public bool LocalStorageAvailable { get; set; }
    public int LocalStorageSize { get; set; }
    public string? LocalStorageError { get; set; }
}
