using System;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel : Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public string Title { get; set; }
    [Reactive] public string MobileNumber { get; set; }
    [Reactive] public string Nickname { get; set; }

    public ReactiveCommand<Unit, Unit> EditProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    public SettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        Title = "Settings";
        MobileNumber = "+1 234 567 8900"; // TODO: Get from NetworkProvider.ApplicationInstanceSettings
        Nickname = "User"; // TODO: Get from NetworkProvider.ApplicationInstanceSettings

        EditProfileCommand = ReactiveCommand.Create(() =>
        {
            // TODO: Navigate to edit profile
        });

        LogoutCommand = ReactiveCommand.Create(() =>
        {
            // TODO: Wire up to MasterViewModel.LogoutCommand
        });
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
