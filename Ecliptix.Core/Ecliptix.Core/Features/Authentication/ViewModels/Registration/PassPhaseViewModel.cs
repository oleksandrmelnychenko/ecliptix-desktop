using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class PassPhaseViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IDisposable, IResettable
{
    private readonly CompositeDisposable _disposables = new();

    private string _passPhase = string.Empty;
    private bool _isDisposed;

    public PassPhaseViewModel(
        ILocalizationService localizationService,
        IScreen hostScreen, NetworkProvider networkProvider) : base(networkProvider, localizationService)
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
        if (_isDisposed) return;

        PassPhase = string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            SubmitCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
