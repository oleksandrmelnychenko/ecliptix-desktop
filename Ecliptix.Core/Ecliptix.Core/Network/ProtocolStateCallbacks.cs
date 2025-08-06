using System;
using System.Threading.Tasks;
using Ecliptix.Core.Security;
using Ecliptix.Protocol.System.Core;
using Serilog;

namespace Ecliptix.Core.Network;

/// <summary>
/// Callbacks for protocol state changes that require persistence
/// </summary>
public interface IProtocolStateCallbacks
{
    Task OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex);
    Task OnMessageSent(uint connectId, uint messageIndex, bool dhKeyIncluded);
    Task OnMessageReceived(uint connectId, uint messageIndex, bool hasSkippedKeys);
    Task OnChainSynchronized(uint connectId, uint localLength, uint remoteLength);
    Task OnSessionEstablished(uint connectId, bool isRecovery);
    Task OnCriticalError(uint connectId, string error);
}

/// <summary>
/// Implementation that connects protocol events to state persistence
/// </summary>
public class ProtocolStateCallbacks : IProtocolStateCallbacks
{
    private readonly ProtocolStatePersistence _statePersistence;
    private readonly Func<uint, EcliptixProtocolSystem?> _getProtocolSystem;
    private readonly Func<string> _getUserId;

    public ProtocolStateCallbacks(
        ProtocolStatePersistence statePersistence,
        Func<uint, EcliptixProtocolSystem?> getProtocolSystem,
        Func<string> getUserId)
    {
        _statePersistence = statePersistence ?? throw new ArgumentNullException(nameof(statePersistence));
        _getProtocolSystem = getProtocolSystem ?? throw new ArgumentNullException(nameof(getProtocolSystem));
        _getUserId = getUserId ?? throw new ArgumentNullException(nameof(getUserId));
    }

    public async Task OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex)
    {
        Log.Information(
            "[STATE] DH Ratchet performed - ConnectId: {ConnectId}, Type: {Type}, Index: {Index}",
            connectId, isSending ? "Sending" : "Receiving", newIndex);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null)
        {
            Log.Error("Protocol system not found for DH ratchet save!");
            return;
        }

        await _statePersistence.SaveAfterDhRatchetAsync(
            connectId, 
            _getUserId(), 
            protocolSystem, 
            isSending, 
            newIndex);
    }

    public async Task OnMessageSent(uint connectId, uint messageIndex, bool dhKeyIncluded)
    {
        Log.Debug(
            "[STATE] Message sent - ConnectId: {ConnectId}, Index: {Index}, DHKey: {HasDH}",
            connectId, messageIndex, dhKeyIncluded);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null) return;

        // If DH key was included, the DH ratchet callback will handle the save
        // Otherwise, save after message sent
        if (!dhKeyIncluded)
        {
            await _statePersistence.SaveAfterMessageSentAsync(
                connectId, 
                _getUserId(), 
                protocolSystem, 
                messageIndex);
        }
    }

    public async Task OnMessageReceived(uint connectId, uint messageIndex, bool hasSkippedKeys)
    {
        Log.Debug(
            "[STATE] Message received - ConnectId: {ConnectId}, Index: {Index}, Skipped: {Skipped}",
            connectId, messageIndex, hasSkippedKeys);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null) return;

        await _statePersistence.SaveAfterMessageReceivedAsync(
            connectId, 
            _getUserId(), 
            protocolSystem, 
            messageIndex, 
            hasSkippedKeys);
    }

    public async Task OnChainSynchronized(uint connectId, uint localLength, uint remoteLength)
    {
        Log.Information(
            "[STATE] Chain synchronized - ConnectId: {ConnectId}, Local: {Local}, Remote: {Remote}",
            connectId, localLength, remoteLength);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null) return;

        await _statePersistence.SaveAfterChainSyncAsync(
            connectId, 
            _getUserId(), 
            protocolSystem, 
            localLength, 
            remoteLength);
    }

    public async Task OnSessionEstablished(uint connectId, bool isRecovery)
    {
        Log.Information(
            "[STATE] Session established - ConnectId: {ConnectId}, Recovery: {IsRecovery}",
            connectId, isRecovery);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null) return;

        await _statePersistence.SaveAfterSessionEstablishedAsync(
            connectId, 
            _getUserId(), 
            protocolSystem, 
            isRecovery);
    }

    public async Task OnCriticalError(uint connectId, string error)
    {
        Log.Error("[STATE] Critical error, forcing save - ConnectId: {ConnectId}, Error: {Error}",
            connectId, error);

        var protocolSystem = _getProtocolSystem(connectId);
        if (protocolSystem == null) return;

        await _statePersistence.ForceImmediateSaveAsync(
            connectId, 
            _getUserId(), 
            protocolSystem, 
            $"Critical error: {error}");
    }
}