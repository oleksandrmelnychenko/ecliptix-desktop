namespace Ecliptix.Protocol.System.Interfaces;

internal interface IProtocolEventHandler
{
    void OnProtocolStateChanged(uint connectId);
}
