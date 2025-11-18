using System;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Navigation;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Navigation;

public sealed partial class NavigationSidebarViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly ILogoutService _logoutService;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private bool _isDisposed;

    [Reactive] public NavigationMenuItem? SelectedMenuItem { get; set; }
    [Reactive] public bool IsExpanded { get; set; }
    [Reactive] public bool IsProfileMenuOpen { get; set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    public ObservableCollection<NavigationMenuItem> MenuItems { get; }

    public ReactiveCommand<NavigationMenuItem, SystemU> NavigateCommand { get; }
    public ReactiveCommand<SystemU, SystemU> ToggleProfileMenuCommand { get; }
    public ReactiveCommand<SystemU, SystemU> CloseProfileMenuCommand { get; }
    public ReactiveCommand<SystemU, Result<Ecliptix.Utilities.Unit, LogoutFailure>> LogoutCommand { get; }

    public NavigationSidebarViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;

        MenuItems = new ObservableCollection<NavigationMenuItem>
        {
            new NavigationMenuItem
            {
                Id = "home",
                Label = "Feed",
                IconPath = "FeedIconData",
                TooltipText = "Feed",
                Type = NavigationMenuItemType.Regular
            },
            new NavigationMenuItem
            {
                Id = "chats",
                Label = "Chats",
                IconPath = "ChatsIconData",
                TooltipText = "Chats",
                Type = NavigationMenuItemType.Regular
            },
            new NavigationMenuItem
            {
                Id = "settings",
                Label = "Settings",
                IconPath = "SettingsIconData",
                TooltipText = "Settings",
                Type = NavigationMenuItemType.Regular
            }
        };

        SelectedMenuItem = MenuItems[0];

        NavigateCommand = ReactiveCommand.Create<NavigationMenuItem, SystemU>(
            menuItem =>
            {
                if (SelectedMenuItem != menuItem)
                {
                    SelectedMenuItem?.IsSelected = false;
                    SelectedMenuItem = menuItem;
                    menuItem.IsSelected = true;
                }

                return SystemU.Default;
            });

        NavigateCommand
            .Subscribe()
            .DisposeWith(_disposables);

        SelectedMenuItem?.IsSelected = true;

        ToggleProfileMenuCommand = ReactiveCommand.Create(() =>
        {
            IsProfileMenuOpen = !IsProfileMenuOpen;
            return SystemU.Default;
        });

        CloseProfileMenuCommand = ReactiveCommand.Create(() =>
        {
            IsProfileMenuOpen = false;
            return SystemU.Default;
        });

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        LogoutCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                CancelLogoutOperation();

                CancellationTokenSource operationCts = new();
                _logoutCancellationTokenSource = operationCts;

                try
                {
                    Result<Ecliptix.Utilities.Unit, LogoutFailure> result = await _logoutService.LogoutAsync(
                        LogoutReason.USER_INITIATED,
                        operationCts.Token).ConfigureAwait(false);
                    return result;
                }
                catch (TimeoutException ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout timed out - secrecy channel not restored.", ex));
                }
                catch (OperationCanceledException ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout cancelled.", ex));
                }
                catch (Exception ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout failed due to an unexpected error.", ex));
                }
                finally
                {
                    if (ReferenceEquals(_logoutCancellationTokenSource, operationCts))
                    {
                        _logoutCancellationTokenSource = null;
                    }

                    operationCts.Dispose();
                }
            },
            canLogout);

        LogoutCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy).DisposeWith(_disposables);

        LogoutCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                Log.Error("[NAVIGATION-SIDEBAR-VM] Logout failed: {Message}", error.Message);
            })
            .DisposeWith(_disposables);

        _disposables.Add(LogoutCommand);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelLogoutOperation();
            NavigateCommand?.Dispose();
            ToggleProfileMenuCommand?.Dispose();
            CloseProfileMenuCommand?.Dispose();
            LogoutCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    private void CancelLogoutOperation()
    {
        CancellationTokenSource? logoutSource = Interlocked.Exchange(ref _logoutCancellationTokenSource, null);
        if (logoutSource == null)
        {
            return;
        }

        try
        {
            logoutSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Intentionally suppressed: Logout cancellation token source already disposed
        }
        finally
        {
            logoutSource.Dispose();
        }
    }
}
