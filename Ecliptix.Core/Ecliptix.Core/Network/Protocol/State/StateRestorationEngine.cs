using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Core.Configuration;
using Ecliptix.Core.Network.Protocol.Recovery;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network.Protocol.State;

public class StateRestorationEngine(
    StateRestorationConfiguration config,
    ISecureStorageProvider secureStorage)
{
    public async Task<Result<RestorationResult, NetworkFailure>> RestoreConnectionStateAsync(
        uint connectId,
        Func<EcliptixSecrecyChannelState, Task<Result<bool, NetworkFailure>>> serverRestoreFunc,
        Func<uint, Task<Result<EcliptixSecrecyChannelState, NetworkFailure>>> freshConnectFunc,
        CancellationToken cancellationToken = default)
    {
        DateTime startTime = DateTime.UtcNow;
        Log.Information("Starting advanced state restoration for connection {ConnectId} using strategy {Strategy}",
            connectId, config.PreferredStrategy);

        try
        {
            RestorationStrategy strategy = await DetermineOptimalStrategy(connectId);
            Log.Debug("Selected restoration strategy: {Strategy}", strategy);

            Result<RestorationResult, NetworkFailure> result = strategy switch
            {
                RestorationStrategy.LocalFirst => await RestoreLocalFirst(connectId, serverRestoreFunc, freshConnectFunc),
                RestorationStrategy.ServerFirst => await RestoreServerFirst(connectId, freshConnectFunc),
                RestorationStrategy.Hybrid => await RestoreHybrid(connectId, serverRestoreFunc, freshConnectFunc, cancellationToken),
                RestorationStrategy.Fresh => await RestoreFresh(connectId, freshConnectFunc),
                _ => throw new ArgumentOutOfRangeException()
            };

            TimeSpan duration = DateTime.UtcNow - startTime;
            if (result.IsOk)
            {
                RestorationResult finalResult = result.Unwrap() with { Duration = duration, StrategyUsed = strategy };
                Log.Information("Advanced state restoration completed for {ConnectId} - Success: {Success}, Duration: {Duration}ms",
                    connectId, finalResult.Success, finalResult.Duration.TotalMilliseconds);
                return Result<RestorationResult, NetworkFailure>.Ok(finalResult);
            }

            RestorationResult failureResult = new()
            {
                Success = false,
                StrategyUsed = strategy,
                Duration = duration,
                ErrorMessage = result.UnwrapErr().Message
            };
            return Result<RestorationResult, NetworkFailure>.Ok(failureResult);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Advanced state restoration failed with exception for connection {ConnectId}", connectId);
            return Result<RestorationResult, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Advanced restoration failed: {ex.Message}"));
        }
    }

    private async Task<RestorationStrategy> DetermineOptimalStrategy(uint connectId)
    {
        Result<Option<byte[]>, InternalServiceApiFailure> localStateResult = await GetLocalState(connectId);
        
        if (localStateResult.IsErr || !localStateResult.Unwrap().HasValue)
        {
            Log.Debug("No local state found, using Fresh strategy");
            return RestorationStrategy.Fresh;
        }

        DateTime? stateTimestamp = await GetStateTimestamp(connectId);
        if (stateTimestamp.HasValue)
        {
            TimeSpan stateAge = DateTime.UtcNow - stateTimestamp.Value;
            if (stateAge > config.LocalStateMaxAge)
            {
                Log.Debug("Local state is too old ({Age}), using ServerFirst strategy", stateAge);
                return RestorationStrategy.ServerFirst;
            }
            
            Log.Debug("Local state age is acceptable ({Age}), using preferred strategy", stateAge);
        }
        else
        {
            Log.Debug("No timestamp found for local state, using preferred strategy");
        }

        return config.PreferredStrategy;
    }

    private async Task<Result<RestorationResult, NetworkFailure>> RestoreLocalFirst(
        uint connectId,
        Func<EcliptixSecrecyChannelState, Task<Result<bool, NetworkFailure>>> serverRestoreFunc,
        Func<uint, Task<Result<EcliptixSecrecyChannelState, NetworkFailure>>> freshConnectFunc)
    {
        Log.Debug("Attempting LocalFirst restoration for connection {ConnectId}", connectId);

        Result<Option<byte[]>, InternalServiceApiFailure> localStateResult = await GetLocalState(connectId);
        if (localStateResult.IsErr || !localStateResult.Unwrap().HasValue)
        {
            Log.Debug("No local state available, falling back to fresh connection");
            return await RestoreFresh(connectId, freshConnectFunc);
        }

        try
        {
            EcliptixSecrecyChannelState state = EcliptixSecrecyChannelState.Parser.ParseFrom(localStateResult.Unwrap().Value!);
            Result<bool, NetworkFailure> restoreResult = await serverRestoreFunc(state);

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                return Result<RestorationResult, NetworkFailure>.Ok(new RestorationResult
                {
                    Success = true,
                    StateWasSynced = true
                });
            }

            Log.Debug("Local state restoration failed, establishing fresh connection");
            return await RestoreFresh(connectId, freshConnectFunc);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LocalFirst restoration failed, falling back to fresh");
            return await RestoreFresh(connectId, freshConnectFunc);
        }
    }

    private async Task<Result<RestorationResult, NetworkFailure>> RestoreServerFirst(
        uint connectId,
        Func<uint, Task<Result<EcliptixSecrecyChannelState, NetworkFailure>>> freshConnectFunc)
    {
        Log.Debug("Attempting ServerFirst restoration for connection {ConnectId}", connectId);
        return await RestoreFresh(connectId, freshConnectFunc);
    }

    private async Task<Result<RestorationResult, NetworkFailure>> RestoreHybrid(
        uint connectId,
        Func<EcliptixSecrecyChannelState, Task<Result<bool, NetworkFailure>>> serverRestoreFunc,
        Func<uint, Task<Result<EcliptixSecrecyChannelState, NetworkFailure>>> freshConnectFunc,
        CancellationToken cancellationToken)
    {
        Log.Debug("Attempting Hybrid restoration for connection {ConnectId}", connectId);

        Result<Option<byte[]>, InternalServiceApiFailure> localStateResult = await GetLocalState(connectId);
        if (localStateResult.IsErr || !localStateResult.Unwrap().HasValue)
        {
            return await RestoreFresh(connectId, freshConnectFunc);
        }

        try
        {
            EcliptixSecrecyChannelState state = EcliptixSecrecyChannelState.Parser.ParseFrom(localStateResult.Unwrap().Value!);
            
            using CancellationTokenSource timeoutSource = new(config.StateSyncTimeout);
            using CancellationTokenSource combinedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            Result<bool, NetworkFailure> restoreResult = await serverRestoreFunc(state);

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                return Result<RestorationResult, NetworkFailure>.Ok(new RestorationResult
                {
                    Success = true,
                    StateWasSynced = true
                });
            }

            Log.Debug("Hybrid restoration - local state sync failed, establishing fresh connection");
            return await RestoreFresh(connectId, freshConnectFunc);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hybrid restoration failed, falling back to fresh");
            return await RestoreFresh(connectId, freshConnectFunc);
        }
    }

    private async Task<Result<RestorationResult, NetworkFailure>> RestoreFresh(
        uint connectId,
        Func<uint, Task<Result<EcliptixSecrecyChannelState, NetworkFailure>>> freshConnectFunc)
    {
        Log.Debug("Establishing fresh connection for {ConnectId}", connectId);

        try
        {
            Result<EcliptixSecrecyChannelState, NetworkFailure> freshResult = await freshConnectFunc(connectId);

            if (freshResult.IsOk)
            {
                EcliptixSecrecyChannelState newState = freshResult.Unwrap();
                
                await secureStorage.StoreAsync(connectId.ToString(), newState.ToByteArray());

                return Result<RestorationResult, NetworkFailure>.Ok(new RestorationResult
                {
                    Success = true,
                    RequiredFreshConnection = true
                });
            }

            return Result<RestorationResult, NetworkFailure>.Err(freshResult.UnwrapErr());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fresh connection establishment failed for {ConnectId}", connectId);
            return Result<RestorationResult, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Fresh connection failed: {ex.Message}"));
        }
    }

    private async Task<Result<Option<byte[]>, InternalServiceApiFailure>> GetLocalState(uint connectId)
    {
        return await secureStorage.TryGetByKeyAsync(connectId.ToString());
    }
    
    private async Task<DateTime?> GetStateTimestamp(uint connectId)
    {
        try
        {
            string timestampKey = $"{connectId}_timestamp";
            Result<Option<byte[]>, InternalServiceApiFailure> timestampResult = 
                await secureStorage.TryGetByKeyAsync(timestampKey);
                
            if (timestampResult.IsOk && timestampResult.Unwrap().HasValue)
            {
                byte[] timestampBytes = timestampResult.Unwrap().Value!;
                if (timestampBytes.Length == 8)
                {
                    long timestampTicks = BitConverter.ToInt64(timestampBytes, 0);
                    return new DateTime(timestampTicks, DateTimeKind.Utc);
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error retrieving state timestamp for connection {ConnectId}", connectId);
            return null;
        }
    }
    
    public async Task StoreStateTimestamp(uint connectId, DateTime timestamp)
    {
        try
        {
            string timestampKey = $"{connectId}_timestamp";
            byte[] timestampBytes = BitConverter.GetBytes(timestamp.Ticks);
            await secureStorage.StoreAsync(timestampKey, timestampBytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error storing state timestamp for connection {ConnectId}", connectId);
        }
    }
}