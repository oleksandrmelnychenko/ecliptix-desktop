using System;
using Ecliptix.Core.Network;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core.ViewModels;

public class ViewModelBase : ReactiveObject, IDisposable
{
    private bool _disposedValue;

    protected static uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;

        uint connectId = Utilities.ComputeUniqueConnectId(
            appInstanceInfo.AppInstanceId,
            appInstanceInfo.DeviceId, pubKeyExchangeType);

        return connectId;
    }

    protected static Guid? SystemDeviceIdentifier()
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;
        return appInstanceInfo.SystemDeviceIdentifier;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}