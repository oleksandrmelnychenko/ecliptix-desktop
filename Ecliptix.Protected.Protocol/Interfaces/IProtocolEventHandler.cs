namespace Ecliptix.Protected.Protocol.Interfaces;

internal interface IProtocolEventHandler
{
    void OnProtocolStateChanged(uint connectId);
}
