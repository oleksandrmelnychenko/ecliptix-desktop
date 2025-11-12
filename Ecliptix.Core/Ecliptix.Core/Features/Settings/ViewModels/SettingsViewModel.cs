using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel : Core.MVVM.ViewModelBase
{
    private readonly ILogoutService _logoutService;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private bool _isDisposed;

    [Reactive] public string Title { get; set; }
    [Reactive] public string MobileNumber { get; set; }
    [Reactive] public string Nickname { get; set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<SystemU, SystemU> EditProfileCommand { get; }
    public ReactiveCommand<SystemU, Result<Ecliptix.Utilities.Unit, LogoutFailure>> LogoutCommand { get; }

    public SettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;

        Title = "Settings";
        MobileNumber = "+1 234 567 8900"; // TODO: Get from NetworkProvider.ApplicationInstanceSettings
        Nickname = "User"; // TODO: Get from NetworkProvider.ApplicationInstanceSettings

        EditProfileCommand = ReactiveCommand.Create(() =>
        {
            // TODO: Navigate to edit profile
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
            // Intentionally suppressed: Logout cancellation token source already disposed
        }
        finally
        {
            logoutSource.Dispose();
        }
    }
}
