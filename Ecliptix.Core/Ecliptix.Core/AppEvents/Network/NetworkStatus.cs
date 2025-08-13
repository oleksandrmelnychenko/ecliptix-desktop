namespace Ecliptix.Core.AppEvents.Network;

public enum NetworkStatus
{
    DataCenterConnected,
    DataCenterDisconnected,
    DataCenterConnecting,
    RestoreSecrecyChannel,
    RetriesExhausted,
    ServerShutdown,
    ConnectionRecovering,
    ConnectionRestored
}
