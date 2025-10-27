using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;

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
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private bool _isDisposed;

    [ObservableAsProperty] public bool IsBusy { get; }

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }

    public ReactiveCommand<SystemU, Result<Unit, LogoutFailure>> LogoutCommand { get; }

    public MasterViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        MainWindowViewModel mainWindowViewModel)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;
        ConnectivityNotification = mainWindowViewModel.ConnectivityNotification;

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        LogoutCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                CancelLogoutOperation();

                CancellationTokenSource operationCts = new();
                _logoutCancellationTokenSource = operationCts;

                try
                {
                    Result<Unit, LogoutFailure> result = await _logoutService.LogoutAsync(
                        LogoutReason.UserInitiated,
                        operationCts.Token).ConfigureAwait(false);
                    return result;
                }
                catch (OperationCanceledException ex)
                {
                    return Result<Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout cancelled.", ex));
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
                Log.Error("Logout failed: {Message}", error.Message);
            })
            .DisposeWith(_disposables);

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
