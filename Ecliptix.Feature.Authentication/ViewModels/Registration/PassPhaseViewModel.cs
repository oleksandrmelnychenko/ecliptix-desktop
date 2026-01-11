using System;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration;

public sealed class PassPhaseViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IDisposable, IResettable
{
    private readonly CompositeDisposable _disposables = new();

    private string _passPhase = string.Empty;
    private bool _isDisposed;

    public PassPhaseViewModel(
        ILocalizationService localizationService,
        IScreen hostScreen,
        NetworkProvider networkProvider,
        IGlobalModalService globalModalService) : base(networkProvider, localizationService, globalModalService)
    {
        HostScreen = hostScreen;
        SubmitCommand = ReactiveCommand.Create(() => { });

        _disposables.Add(SubmitCommand);
    }

    public string? UrlPathSegment { get; } = "/pass-phase";
    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }

    public string PassPhase
    {
        get => _passPhase;
        set => this.RaiseAndSetIfChanged(ref _passPhase, value);
    }

    public void ResetState()
    {
        if (_isDisposed)
        {
            return;
        }

        PassPhase = string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
