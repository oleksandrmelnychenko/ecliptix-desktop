using System;
using Ecliptix.Core.Network;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core.ViewModels;

public class ViewModelBase : ReactiveObject, IDisposable
{
    private bool _disposedValue;

    protected static uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        ApplicationInstanceSettings appInstanceInfo = Locator.Current.GetService<ApplicationInstanceSettings>()!;

        uint connectId = Utilities.ComputeUniqueConnectId(
            appInstanceInfo.AppInstanceId.Span,
            appInstanceInfo.DeviceId.Span, pubKeyExchangeType);

        return connectId;
    }

    protected static byte[] ServerPublicKey()
    {
        ApplicationInstanceSettings appInstanceInfo = Locator.Current.GetService<ApplicationInstanceSettings>()!;
        return appInstanceInfo.ServerPublicKey.ToByteArray();
    }

    protected static string? SystemDeviceIdentifier()
    {
        ApplicationInstanceSettings appInstanceInfo = Locator.Current.GetService<ApplicationInstanceSettings>()!;
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