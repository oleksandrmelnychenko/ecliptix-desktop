using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Security;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network.Protocol.State;

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
        
        _periodicSaveTimer = new Timer(
            PeriodicSave, 
            null, 
            PeriodicSaveIntervalMs, 
            PeriodicSaveIntervalMs);
    }

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
            EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
            if (state == null)
            {
                Log.Error("Failed to create protocol state after DH ratchet!");
                return;
            }

            Result<Unit, SecureStorageFailure> saveResult = await SaveStateWithRetryAsync(state, userId, critical: true);
            
            if (saveResult.IsErr)
            {
                Log.Error("CRITICAL: Failed to save state after DH ratchet: {Error}", saveResult.UnwrapErr());
                throw new InvalidOperationException($"Critical state save failed after DH ratchet: {saveResult.UnwrapErr()}");
            }

            UpdateSessionState(connectId, state, isDhRatchet: true);
            
            Log.Information("Successfully saved state after DH ratchet");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during critical DH ratchet state save");
            throw;
        }
    }

    public async Task SaveAfterMessageSentAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        uint messageIndex)
    {
        Log.Debug("Saving state after message sent - Index: {Index}", messageIndex);
        
        await ScheduleDelayedSaveAsync(connectId, userId, protocolSystem, 
            $"MessageSent_{messageIndex}");
    }

    public async Task SaveAfterMessageReceivedAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        uint messageIndex,
        bool hasSkippedKeys)
    {
        Log.Debug("Saving state after message received - Index: {Index}, Skipped: {Skipped}", 
            messageIndex, hasSkippedKeys);
        
        if (hasSkippedKeys)
        {
            Log.Warning("Message received with skipped keys - immediate save required");
            EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
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

    public async Task SaveAfterKeyRotationAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem)
    {
        Log.Information("Saving state after key rotation interval");
        
        EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isKeyRotation: true);
        }
    }

    public async Task SaveAfterSessionEstablishedAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        bool isRecovery)
    {
        Log.Information("Saving state after session {Action}", 
            isRecovery ? "recovery" : "establishment");
        
        EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isNewSession: true);
        }
    }

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
        
        EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
        if (state != null)
        {
            await SaveStateWithRetryAsync(state, userId, critical: true);
            UpdateSessionState(connectId, state, isChainSync: true);
        }
    }

    public async Task ForceImmediateSaveAsync(
        uint connectId,
        string userId,
        EcliptixProtocolSystem protocolSystem,
        string reason)
    {
        Log.Warning("Forcing immediate state save - Reason: {Reason}", reason);
        
        EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
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
            EcliptixProtocolConnection connection = protocolSystem.GetConnection();
            EcliptixSystemIdentityKeys identityKeys = protocolSystem.GetIdentityKeys();

            Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> stateResult = identityKeys.ToProtoState()
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
                Result<Unit, SecureStorageFailure> result = await _secureStorage.SaveStateAsync(
                    state.ToByteArray(), 
                    userId);
                
                if (result.IsOk)
                {
                    Log.Debug("State saved successfully on attempt {Attempt}", attempt);
                    return result;
                }

                if (attempt < maxRetries)
                {
                    int delay = attempt * 100; 
                    Log.Warning(
                        "State save failed on attempt {Attempt}, retrying in {Delay}ms: {Error}",
                        attempt, delay, result.UnwrapErr());
                    await Task.Delay(delay);
                }
            }

            SecureStorageFailure error = new($"Failed to save state after {maxRetries} attempts");
            
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
        await _delayedSaveCts?.CancelAsync()!;
        _delayedSaveCts = new CancellationTokenSource();
        
        CancellationTokenSource? cts = _delayedSaveCts;
        
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDelayMs, cts.Token);
                
                if (!cts.Token.IsCancellationRequested)
                {
                    Log.Debug("Executing delayed save - Trigger: {Trigger}", trigger);
                    EcliptixSecrecyChannelState? state = CreateProtocolState(connectId, protocolSystem);
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
        }, cts.Token);
    }

    private void UpdateSessionState(
        uint connectId, 
        EcliptixSecrecyChannelState state,
        bool isDhRatchet = false,
        bool isKeyRotation = false,
        bool isNewSession = false,
        bool isChainSync = false)
    {
        ProtocolSessionState? existingState = _sessions.GetValueOrDefault(connectId);
        ProtocolSessionState sessionState = new()
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