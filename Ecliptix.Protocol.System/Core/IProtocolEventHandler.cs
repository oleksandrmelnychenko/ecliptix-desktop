namespace Ecliptix.Protocol.System.Core;

internal interface IProtocolEventHandler
{
    void OnProtocolStateChanged(uint connectId);
}
