namespace Ecliptix.Protocol.System.Core;

public interface IProtocolEventHandler
{
    void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex);
    void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength);
    void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys);
}
