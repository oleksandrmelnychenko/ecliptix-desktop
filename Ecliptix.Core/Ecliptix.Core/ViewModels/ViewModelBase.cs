using System;
using System.Collections.Frozen;
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

    protected uint ComputeConnectId()
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

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
    
    protected FrozenSet<string> FilteredErrorMessages { get; } = new[]
    {
        "Connection unavailable - server may be recovering",
        "Data center not responding",
        "Server shutdown detected",
        "Connection recovery initiated",
        "Duplicate request rejected",
        "Request too frequent, please wait",
        "Unexpected error during request",
        "Request cancelled during outage recovery wait",
        
        "All operations exhausted, manual retry required",
        "Operation cancelled",
        "Connection unavailable - server may be recovering",
        "Session not found on server",
        "Provider is shutting down",
        "Client streaming is not yet implemented",
        "Bidirectional streaming is not yet implemented",
        "Connection ID is required",
        
        "Application instance settings not available",
        "No stored state for immediate recovery",
        "Failed to save protocol state",
        "Protocol state saved successfully",
        "Failed to create protocol state",
        "Exception creating protocol state",
        "Error saving protocol state",
        
        "InvokeServiceRequestAsync failed",
        "RPC call failed", 
        "Decryption failed",
        "Stream decryption failed",
        "Stream item error",
        "Expected SingleCall flow but received",
        "Expected InboundStream flow but received",
        "Expected OutboundSink flow but received",
        "Expected BidirectionalStream flow but received",
        "Unsupported flow type",
        "Client streaming is not yet implemented",
        "Bidirectional streaming is not yet implemented",
        
        "Cryptographic desync detected",
        "Chain rotation mismatch detected",
        "Protocol state mismatch detected",
        "Failed to parse stored state",
        "Restoration failed, awaiting manual retry",
        "Session not found on server",
        "Protocol sync failed",
        "Fresh establishment failed",
        "Resynchronization failed",
        
        "Invalid request type"
    }.ToFrozenSet();
}