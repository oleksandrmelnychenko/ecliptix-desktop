using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels;

public abstract class ViewModelBase
    : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ILocalizationService LocalizationService { get; }

    protected ISystemEvents SystemEvents { get; }
    protected NetworkProvider NetworkProvider { get; }

    private bool _disposedValue;

    public ViewModelActivator Activator { get; } = new();

    protected ViewModelBase(ISystemEvents systemEvents, NetworkProvider networkProvider,
        ILocalizationService localizationService)
    {
        SystemEvents = systemEvents;
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
    
    protected Membership Membership()
    {
        return NetworkProvider.ApplicationInstanceSettings.Membership;
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