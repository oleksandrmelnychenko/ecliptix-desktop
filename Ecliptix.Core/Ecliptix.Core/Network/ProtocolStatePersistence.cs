using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Security;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network;

/// <summary>
/// Manages protocol state persistence at critical points during Double Ratchet operation
/// Ensures state is saved after every security-critical operation
/// </summary>
public class ProtocolStatePersistence : IDisposable
{
    private readonly SecureStateStorage _secureStorage;
    private readonly ConcurrentDictionary<uint, ProtocolSessionState> _sessions;
    private readonly SemaphoreSlim _saveSemaphore;
    private readonly Timer _periodicSaveTimer;
    private readonly object _syncLock = new();
    private bool _disposed;

    // Critical save triggers
    private const int SaveDelayMs = 100; // Small delay to batch rapid changes
    private const int PeriodicSaveIntervalMs = 30000; // Periodic save every 30 seconds
    
    public ProtocolStatePersistence(SecureStateStorage secureStorage)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _sessions = new ConcurrentDictionary<uint, ProtocolSessionState>();
        _saveSemaphore = new SemaphoreSlim(1, 1);
        
        // Periodic save timer for safety
        _periodicSaveTimer = new Timer(
            PeriodicSave, 
            null, 
            PeriodicSaveIntervalMs, 
            PeriodicSaveIntervalMs);
    }

    /// <summary>
    /// CRITICAL: Save state after DH ratchet - Most important save point
    /// Called when:
    /// 1. We generate new DH key pair (sending ratchet)
    /// 2. We receive new DH public key from peer (receiving ratchet)
    /// </summary>
    public async Task SaveAfterDhRatchetAsync(
        uint connectId, 
        string userId,
        EcliptixProtocolSystem protocolSystem,
        bool isSendingRatchet,
        uint newDhIndex)
    {
        Log.Information(
            "[CRITICAL] Saving state after DH ratchet - ConnectId: {ConnectId}, Type: {RatchetType}, NewIndex: {Index}",
            connectId, 
            isSendingRatchet ? "Sending" : "Receiving", 
            newDhIndex);

        try
        {
            var state = CreateProtocolState(connectId, protocolSystem);
            if (state == null)
            {
                Log.Error("Failed to create protocol state after DH ratchet!");
                return;
            }

            // Mark this as a critical save that must succeed
            var saveResult = await SaveStateWithRetryAsync(state, userId, critical: true);
            
            if (saveResult.IsErr)
            {
                Log.Error("CRITICAL: Failed to save state after DH ratchet: {Error}", saveResult.UnwrapErr());
                // This is critical - consider halting protocol operations
                throw new InvalidOperationException($"Critical state save failed after DH ratchet: {saveResult.UnwrapErr()}");
            }

            // Update session tracking
            UpdateSessionState(connectId, state, isDhRatchet: true);
            
            Log.Information("Successfully saved state after DH ratchet");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during critical DH ratchet state save");
            throw;
        }
    }

    /// <summary>
    /// Save state after sending a message (chain key advanced)
    /// </summary>
    public async Task SaveAfterMessageSentAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        uint messageIndex)
    {
        Log.Debug("Saving state after message sent - Index: {Index}", messageIndex);
        
        // Use delayed save for non-critical updates
        await ScheduleDelayedSaveAsync(connectId, userId, protocolSystem, 
            $"MessageSent_{messageIndex}");
    }

    /// <summary>
    /// Save state after receiving a message (chain key advanced, possible skipped keys)
    /// </summary>
    public async Task SaveAfterMessageReceivedAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        uint messageIndex,
        bool hasSkippedKeys)
    {
        Log.Debug("Saving state after message received - Index: {Index}, Skipped: {Skipped}", 
            messageIndex, hasSkippedKeys);
        
        // If we skipped keys, this is more critical
        if (hasSkippedKeys)
        {
            Log.Warning("Message received with skipped keys - immediate save required");
            var state = CreateProtocolState(connectId, protocolSystem);
            if (state != null)
            {
                await SaveStateWithRetryAsync(state, userId, critical: false);
            }
        }
        else
        {
            // Normal message, use delayed save
            await ScheduleDelayedSaveAsync(connectId, userId, protocolSystem, 
                $"MessageReceived_{messageIndex}");
        }
    }

    /// <summary>
    /// Save state after key rotation interval
    /// </summary>
    public async Task SaveAfterKeyRotationAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem)
    {
        Log.Information("Saving state after key rotation interval");
        
        var state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isKeyRotation: true);
        }
    }

    /// <summary>
    /// Save state after session establishment or recovery
    /// </summary>
    public async Task SaveAfterSessionEstablishedAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        bool isRecovery)
    {
        Log.Information("Saving state after session {Action}", 
            isRecovery ? "recovery" : "establishment");
        
        var state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isNewSession: true);
        }
    }

    /// <summary>
    /// Save state after chain synchronization
    /// </summary>
    public async Task SaveAfterChainSyncAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        uint localChainLength,
        uint remoteChainLength)
    {
        Log.Information(
            "Saving state after chain sync - Local: {Local}, Remote: {Remote}",
            localChainLength, remoteChainLength);
        
        var state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isChainSync: true);
        }
    }

    /// <summary>
    /// Force immediate save of current state
    /// </summary>
    public async Task ForceImmediateSaveAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        string reason)
    {
        Log.Warning("Forcing immediate state save - Reason: {Reason}", reason);
        
        var state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
        }
    }

    private EcliptixSecrecyChannelState? CreateProtocolState(
        uint connectId, 
        EcliptixProtocolSystem protocolSystem)
    {
        try
        {
            var connection = protocolSystem.GetConnection();
            var identityKeys = protocolSystem.GetIdentityKeys();

            var stateResult = identityKeys.ToProtoState()
                .AndThen(identityKeysProto => connection.ToProtoState()
                    .Map(ratchetStateProto => new EcliptixSecrecyChannelState
                    {
                        ConnectId = connectId,
                        IdentityKeys = identityKeysProto,
                        RatchetState = ratchetStateProto
                    }));

            if (stateResult.IsErr)
            {
                Log.Error("Failed to create protocol state: {Error}", stateResult.UnwrapErr());
                return null;
            }

            return stateResult.Unwrap();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception creating protocol state");
            return null;
        }
    }

    private async Task<Result<Unit, SecureStorageFailure>> SaveStateWithRetryAsync(
        EcliptixSecrecyChannelState state,
        string userId,
        bool critical,
        int maxRetries = 3)
    {
        await _saveSemaphore.WaitAsync();
        try
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var result = await _secureStorage.SaveStateAsync(
                    state.ToByteArray(), 
                    userId);
                
                if (result.IsOk)
                {
                    Log.Debug("State saved successfully on attempt {Attempt}", attempt);
                    return result;
                }

                if (attempt < maxRetries)
                {
                    var delay = attempt * 100; // Progressive delay
                    Log.Warning(
                        "State save failed on attempt {Attempt}, retrying in {Delay}ms: {Error}",
                        attempt, delay, result.UnwrapErr());
                    await Task.Delay(delay);
                }
            }

            var error = new SecureStorageFailure($"Failed to save state after {maxRetries} attempts");
            
            if (critical)
            {
                Log.Error("CRITICAL: State save failed after all retries!");
            }
            
            return Result<Unit, SecureStorageFailure>.Err(error);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private CancellationTokenSource? _delayedSaveCts;
    
    private async Task ScheduleDelayedSaveAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        string trigger)
    {
        // Cancel any pending delayed save
        _delayedSaveCts?.Cancel();
        _delayedSaveCts = new CancellationTokenSource();
        
        var cts = _delayedSaveCts;
        
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for a short delay to batch rapid changes
                await Task.Delay(SaveDelayMs, cts.Token);
                
                if (!cts.Token.IsCancellationRequested)
                {
                    Log.Debug("Executing delayed save - Trigger: {Trigger}", trigger);
                    var state = CreateProtocolState(connectId, protocolSystem);
                    if (state != null)
                    {
                        await SaveStateWithRetryAsync(state, userId, critical: false);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when a new save is scheduled
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in delayed save");
            }
        });
    }

    private void UpdateSessionState(
        uint connectId, 
        EcliptixSecrecyChannelState state,
        bool isDhRatchet = false,
        bool isKeyRotation = false,
        bool isNewSession = false,
        bool isChainSync = false)
    {
        var existingState = _sessions.TryGetValue(connectId, out var existing) ? existing : null;
        var sessionState = new ProtocolSessionState
        {
            ConnectId = connectId,
            LastSaved = DateTimeOffset.UtcNow,
            SendingChainLength = state.RatchetState.SendingStep.CurrentIndex,
            ReceivingChainLength = state.RatchetState.ReceivingStep.CurrentIndex,
            LastDhRatchet = isDhRatchet ? DateTimeOffset.UtcNow : existingState?.LastDhRatchet,
            SaveCount = (existingState?.SaveCount ?? 0) + 1
        };

        _sessions.AddOrUpdate(connectId, sessionState, (_, _) => sessionState);
    }

    private void PeriodicSave(object? state)
    {
        if (_disposed) return;
        
        try
        {
            Log.Debug("Periodic save timer triggered");
            // Implementation would save all active sessions
            // This is a safety net for any missed saves
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in periodic save");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _delayedSaveCts?.Cancel();
        _periodicSaveTimer?.Dispose();
        _saveSemaphore?.Dispose();
    }
}

public class ProtocolSessionState
{
    public uint ConnectId { get; set; }
    public DateTimeOffset LastSaved { get; set; }
    public uint SendingChainLength { get; set; }
    public uint ReceivingChainLength { get; set; }
    public DateTimeOffset? LastDhRatchet { get; set; }
    public int SaveCount { get; set; }
}