using System;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Core.Shell.Messaging;
using Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Navigation;

public sealed class NavigationSidebarViewModel : Ecliptix.Core.MVVM.ViewModelBase
{
    private readonly ILogoutService _logoutService;
    private readonly IProfileMenuService _profileMenuService;
    private readonly IApplicationSecureStorageProvider _storageProvider;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private ObservableAsPropertyHelper<bool>? _isBusy;
    private bool _isDisposed;
    private readonly IMessageBus? _messageBus;

    [Reactive] public NavigationMenuItem? SelectedMenuItem { get; set; }

    [Reactive] public bool IsExpanded { get; set; } = true;
    [Reactive] public string UserDisplayName { get; set; } = "@user";
    public bool IsBusy => _isBusy?.Value ?? false;
    [Reactive] public bool IsParentAnimating { get; set; }

    public string AddAccountText => LocalizationService.GetString(LocalizationKeys.ProfileMenu.ADD_ACCOUNT);
    public string LogoutText => LocalizationService.GetString(LocalizationKeys.ProfileMenu.LOGOUT);

    public ObservableCollection<NavigationMenuItem> MenuItems { get; }

    public ReactiveCommand<NavigationMenuItem, SystemU> NavigateCommand { get; }
    public ReactiveCommand<SystemU, SystemU> ToggleProfileMenuCommand { get; }
    public ReactiveCommand<SystemU, Result<Unit, LogoutFailure>> LogoutCommand { get; }
    public ReactiveCommand<SystemU, bool> ToggleSidebarCommand { get; }
    public ReactiveCommand<SystemU, SystemU> OpenCreateDialogCommand { get; }

    public NavigationMenuItem ProfileMenuItem { get; }

    public NavigationSidebarViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IProfileMenuService profileMenuService,
        IApplicationSecureStorageProvider storageProvider)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;
        _profileMenuService = profileMenuService;
        _storageProvider = storageProvider;
        _messageBus = Locator.Current?.GetService<IMessageBus>();

        IsExpanded = true;

        IObservable<bool> canNavigate = this.WhenAnyValue(
                x => x.IsParentAnimating,
                x => x.IsBusy,
                (isAnimating, isBusy) => !isAnimating && !isBusy
            )
            .DistinctUntilChanged();

        OpenCreateDialogCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_messageBus != null)
            {
                await _messageBus.PublishAsync(new OpenCreateWizardEvent());
            }
        });

        if (_messageBus != null)
        {
            _messageBus.Subscribe<ToggleSidebarEvent>(async evt =>
            {
                IsExpanded = !IsExpanded;
            }, SubscriptionLifetime.STRONG).DisposeWith(_disposables);
        }

        ToggleSidebarCommand = ReactiveCommand.Create(() =>
        {
            IsExpanded = !IsExpanded;
            return IsExpanded;
        });

        MenuItems = new ObservableCollection<NavigationMenuItem>
        {
            new()
            {
                Id = "feed",
                Label = "Feed",
                IconPath = "HomeIconData",
                TooltipText = "Feed",
                Type = NavigationMenuItemType.REGULAR,
                NotificationCount = 3
            },
            new()
            {
                Id = "chats",
                Label = "Chats",
                IconPath = "ChatsIconData",
                TooltipText = "Chats",
                Type = NavigationMenuItemType.REGULAR,
                NotificationCount = 12
            },
            new()
            {
                Id = "settings",
                Label = "Settings",
                IconPath = "SettingsIconData",
                TooltipText = "Settings",
                Type = NavigationMenuItemType.REGULAR,
                NotificationCount = 0
            }
        };

        ProfileMenuItem = new NavigationMenuItem
        {
            Id = "profile",
            Label = "Profile",
            Type = NavigationMenuItemType.REGULAR
        };

        SelectedMenuItem = MenuItems[0];

        NavigateCommand = ReactiveCommand.CreateFromTask<NavigationMenuItem>(
            async menuItem =>
            {
                if (SelectedMenuItem == menuItem)
                {
                    return;
                }

                SelectedMenuItem?.IsSelected = false;
                SelectedMenuItem = menuItem;
                SelectedMenuItem?.IsSelected = true;
            },
            canNavigate
        );

        NavigateCommand
            .Subscribe()
            .DisposeWith(_disposables);

        SelectedMenuItem?.IsSelected = true;

        ToggleProfileMenuCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _profileMenuService.ToggleAsync();
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

        _isBusy = LogoutCommand.IsExecuting.ToProperty(this, x => x.IsBusy);
        _disposables.Add(_isBusy);

        LogoutCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                Log.Error("[NAVIGATION-SIDEBAR-VM] Logout failed: {Message}", error.Message);
            })
            .DisposeWith(_disposables);

        _disposables.Add(LogoutCommand);

        LoadUserDataAsync().ConfigureAwait(false);
    }

    private async Task LoadUserDataAsync()
    {
        try
        {
            Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
                await _storageProvider.GetApplicationInstanceSettingsAsync();

            if (settingsResult.IsOk)
            {
                ApplicationInstanceSettings settings = settingsResult.Unwrap();

                if (settings.CurrentAccountId != null && !settings.CurrentAccountId.IsEmpty)
                {
                    string guidString;
                    if (settings.CurrentAccountId.Length == 16)
                    {
                        Span<byte> bytes = stackalloc byte[16];
                        settings.CurrentAccountId.Span.CopyTo(bytes);
                        Guid guid = new(bytes);
                        Span<char> chars = stackalloc char[8];
                        guid.TryFormat(chars, out _, "N");
                        guidString = new string(chars);
                    }
                    else
                    {
                        guidString = "user";
                    }
                    UserDisplayName = $"@{guidString}";
                }
                else
                {
                    UserDisplayName = "@user";
                }

                Log.Information("[NAVIGATION-SIDEBAR-VM] User data loaded successfully");
            }
            else
            {
                Log.Warning("[NAVIGATION-SIDEBAR-VM] Failed to load user data: {Error}", settingsResult.UnwrapErr().Message);
                UserDisplayName = "@user";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NAVIGATION-SIDEBAR-VM] Error loading user data");
            UserDisplayName = "@user";
        }
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

        }
        finally
        {
            logoutSource.Dispose();
        }
    }
}
