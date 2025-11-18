using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Account;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

//TODO temp

public class AppearanceSettingsViewModel : ReactiveObject { }
public class SecuritySettingsViewModel : ReactiveObject { }

public sealed partial class SettingsViewModel : Core.MVVM.ViewModelBase, IActivatableViewModel
{
    private readonly ILogoutService _logoutService;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private bool _isDisposed;

    [Reactive] public object CurrentSettingsPage { get; set; }

    public AccountSettingsViewModel AccountSettings { get; private set; }
    public AppearanceSettingsViewModel AppearanceSettings { get; private set; }
    public SecuritySettingsViewModel SecuritySettings { get; private set; }

    public ReactiveCommand<string, SystemU> NavigateCommand { get; private set; }
    public ReactiveCommand<SystemU, Result<EUnit, LogoutFailure>> LogoutCommand { get; }

    [ObservableAsProperty] public bool IsBusy { get; }
    public ViewModelActivator Activator { get; } = new();

    public SettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IApplicationSecureStorageProvider secureStorageProvider)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        AccountSettings = new AccountSettingsViewModel(networkProvider, localizationService, secureStorageProvider);
        AppearanceSettings = new AppearanceSettingsViewModel();
        SecuritySettings = new SecuritySettingsViewModel();

        CurrentSettingsPage = AccountSettings;

        NavigateCommand = ReactiveCommand.Create<string>(page =>
        {
            CurrentSettingsPage = page switch
            {
                "Account" => AccountSettings,
                "Appearance" => AppearanceSettings,
                "Security" => SecuritySettings,
                _ => AccountSettings
            };
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

        LogoutCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy).DisposeWith(_disposables);

        LogoutCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                Log.Error("[SETTINGS-VM] Logout failed: {Message}", error.Message);
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
            // Intentionally suppressed
        }
        finally
        {
            logoutSource.Dispose();
        }
    }

}
