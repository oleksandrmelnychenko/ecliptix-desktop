using System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core.ViewModels;

public abstract class ViewModelBase(NetworkProvider networkProvider) : ReactiveObject, IDisposable
{
    protected NetworkProvider NetworkProvider { get; } = networkProvider;

    private bool _disposedValue;

    protected uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        uint connectId = Helpers.ComputeUniqueConnectId(
            NetworkProvider.ApplicationInstanceSettings.AppInstanceId.Span,
            NetworkProvider.ApplicationInstanceSettings.DeviceId.Span, pubKeyExchangeType);

        return connectId;
    }

    protected byte[] ServerPublicKey() =>
        NetworkProvider.ApplicationInstanceSettings.ServerPublicKey.ToByteArray();

    protected string SystemDeviceIdentifier() =>
        NetworkProvider.ApplicationInstanceSettings.SystemDeviceIdentifier;

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