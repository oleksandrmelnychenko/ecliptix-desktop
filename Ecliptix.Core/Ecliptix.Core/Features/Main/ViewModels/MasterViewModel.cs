using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Main.ViewModels;

public sealed class MasterViewModel : Core.MVVM.ViewModelBase, IDisposable
{
    private readonly ILogoutService _logoutService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [ObservableAsProperty] public bool IsBusy { get; }

    public NetworkStatusNotificationViewModel NetworkStatusNotification { get; }

    public ReactiveCommand<SystemU, Result<Unit, LogoutFailure>> LogoutCommand { get; }

    public MasterViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        MainWindowViewModel mainWindowViewModel)
        : base(systemEventService, networkProvider, localizationService)
    {
        _logoutService = logoutService;
        _mainWindowViewModel = mainWindowViewModel;
        NetworkStatusNotification = mainWindowViewModel.NetworkStatusNotification;

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        LogoutCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                Result<Unit, LogoutFailure> result = await _logoutService.LogoutAsync(
                    LogoutReason.UserInitiated);
                return result;
            },
            canLogout);

        LogoutCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy).DisposeWith(_disposables);

        LogoutCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                Log.Error("Logout failed: {Message}", error.Message);
            })
            .DisposeWith(_disposables);

        LogoutCommand
            .Where(result => result.IsOk)
            .Subscribe(_ =>
            {
                Log.Information("Logout completed successfully");
            })
            .DisposeWith(_disposables);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            LogoutCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
