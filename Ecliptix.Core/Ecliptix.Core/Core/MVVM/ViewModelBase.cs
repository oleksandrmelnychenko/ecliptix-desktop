using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;

namespace Ecliptix.Core.Core.MVVM;

public abstract class ViewModelBase : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ILocalizationService LocalizationService { get; }

    protected ISystemEventService SystemEventService { get; }
    protected NetworkProvider NetworkProvider { get; }

    private bool _disposedValue;

    public ViewModelActivator Activator { get; } = new();

    protected ViewModelBase(ISystemEventService systemEventService, NetworkProvider networkProvider,
        ILocalizationService localizationService)
    {
        SystemEventService = systemEventService;
        NetworkProvider = networkProvider;
        LocalizationService = localizationService;

        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => localizationService.LanguageChanged += handler,
                    handler => localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ => { this.RaisePropertyChanged(string.Empty); })
                .DisposeWith(disposables);
        });
    }

    protected uint ComputeConnectId()
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

        return connectId;
    }

    protected byte[] ServerPublicKey() =>
        SecureByteStringInterop.WithByteStringAsSpan(
            NetworkProvider.ApplicationInstanceSettings.ServerPublicKey,
            span => span.ToArray());

    protected string SystemDeviceIdentifier() =>
        NetworkProvider.ApplicationInstanceSettings.SystemDeviceIdentifier;

    protected Membership Membership() =>
        NetworkProvider.ApplicationInstanceSettings.Membership;

    protected string Culture() =>
        NetworkProvider.ApplicationInstanceSettings.Culture;

    protected string Country() =>
        NetworkProvider.ApplicationInstanceSettings.Country;

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