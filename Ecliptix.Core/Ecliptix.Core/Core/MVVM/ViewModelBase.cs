using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Core.MVVM;

public abstract class ViewModelBase : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ILocalizationService LocalizationService { get; }

    protected ISystemEventService SystemEventService { get; }
    protected NetworkProvider NetworkProvider { get; }

    private bool _disposedValue;

    public ViewModelActivator Activator { get; } = new();
    
    protected IObservable<SystemU> LanguageChanged { get; }

    protected ViewModelBase(ISystemEventService systemEventService, NetworkProvider networkProvider,
        ILocalizationService localizationService)
    {
        SystemEventService = systemEventService;
        NetworkProvider = networkProvider;
        LocalizationService = localizationService;

        LanguageChanged = Observable.Create<SystemU>(observer =>
            {
                observer.OnNext(SystemU.Default);

                void Handler() => observer.OnNext(SystemU.Default);

                localizationService.LanguageChanged += Handler;
                return Disposable.Create(() => localizationService.LanguageChanged -= Handler);
            })
            .Publish()
            .RefCount();
        
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
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                pubKeyExchangeType);

        return connectId;
    }

    protected async Task<Result<uint, NetworkFailure>> EnsureStreamProtocolAsync(
        PubKeyExchangeType streamType)
    {
        Log.Information("[VIEWMODEL] Ensuring protocol for stream type {Type}", streamType);
        return await NetworkProvider.EnsureProtocolForTypeAsync(streamType);
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
    }
}