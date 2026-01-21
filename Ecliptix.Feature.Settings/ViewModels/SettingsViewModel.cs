using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Settings.Domain.Models;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using EUnit = Ecliptix.Utilities.Unit;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Settings.ViewModels;

public class SettingsMenuItem : ReactiveObject
{
    public required SettingsSection Section { get; init; }
    public string Title => Section.Title;
    public string IconData => Section.IconData;

    public required object ViewModel { get; init; }

    [Reactive] public bool IsSelected { get; set; }
}

public sealed partial class SettingsViewModel : Ecliptix.Core.MVVM.ViewModelBase, IActivatableViewModel
{
    private readonly ILogoutService _logoutService;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private ObservableAsPropertyHelper<bool>? _isBusy;
    private bool _isDisposed;

    [Reactive] public object CurrentSettingsPage { get; set; }
    [Reactive] public bool IsTransitionReversed { get; set; }
    public ObservableCollection<SettingsMenuItem> MenuItems { get; }
    public ReactiveCommand<SettingsMenuItem, SystemU> NavigateCommand { get; private set; }

    public ReactiveCommand<SystemU, Result<EUnit, LogoutFailure>> LogoutCommand { get; }

    public bool IsBusy => _isBusy?.Value ?? false;
    public new ViewModelActivator Activator { get; } = new();

    public SettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IApplicationSecureStorageProvider secureStorageProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService)
    {
        _logoutService = logoutService;

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        AccountSettingsViewModel accountVm = new(networkProvider, localizationService, secureStorageProvider, globalModalService);
        AppearanceSettingsViewModel appearanceVm = new();
        SecuritySettingsViewModel securityVm = new();

        SettingsSection[] sections =
        {
            new(SettingsSectionId.Account, "Account",
                "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"),
            new(SettingsSectionId.Appearance, "Appearance",
                "M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9 9-4.03 9-9c0-.46-.04-.92-.1-1.36-.98 1.37-2.58 2.26-4.4 2.26-2.98 0-5.4-2.42-5.4-5.4 0-1.81.89-3.42 2.26-4.4-.44-.06-.9-.1-1.36-.1z"),
            new(SettingsSectionId.Security, "Security",
                "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z")
        };

        object ResolveViewModel(SettingsSectionId id) => id switch
        {
            SettingsSectionId.Account => accountVm,
            SettingsSectionId.Appearance => appearanceVm,
            SettingsSectionId.Security => securityVm,
            _ => accountVm
        };

        MenuItems = new ObservableCollection<SettingsMenuItem>
        {
            new()
            {
                Section = sections[0],
                ViewModel = ResolveViewModel(sections[0].Id),
                IsSelected = true
            },
            new()
            {
                Section = sections[1],
                ViewModel = ResolveViewModel(sections[1].Id)
            },
            new()
            {
                Section = sections[2],
                ViewModel = ResolveViewModel(sections[2].Id)
            }
        };

        CurrentSettingsPage = MenuItems.First(x => x.IsSelected).ViewModel;

        NavigateCommand = ReactiveCommand.Create<SettingsMenuItem>(newItem =>
        {
            SettingsMenuItem? oldItem = MenuItems.FirstOrDefault(x => x.IsSelected);
            int oldIndex = oldItem != null ? MenuItems.IndexOf(oldItem) : 0;
            int newIndex = MenuItems.IndexOf(newItem);

            IsTransitionReversed = newIndex < oldIndex;

            foreach (SettingsMenuItem menuItem in MenuItems)
            {
                menuItem.IsSelected = false;
            }
            newItem.IsSelected = true;

            CurrentSettingsPage = newItem.ViewModel;

        });

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
                Log.Error(error.InnerException, "[SETTINGS-VM] Logout failed: {Message}", error.Message);
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
            LogoutCommand.Dispose();
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
