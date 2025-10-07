using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Protobuf.Protocol;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _mobileNumberIdentifier;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly ILocalizationService _localizationService;
    private readonly IUiDispatcher _uiDispatcher;

    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }

    private readonly Lock _sessionLock = new();

    private Guid _verificationSessionIdentifier = Guid.Empty;

    private Guid? VerificationSessionIdentifier
    {
        get
        {
            lock (_sessionLock)
            {
                return _verificationSessionIdentifier == Guid.Empty ? null : _verificationSessionIdentifier;
            }
        }
    }

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToPasswordConfirmation { get; }

    public new ViewModelActivator Activator { get; } = new();

    [Reactive] public string VerificationCode { get; set; } = string.Empty;
    [Reactive] public bool IsSent { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;
    [Reactive] public string RemainingTime { get; private set; } = AuthenticationConstants.InitialRemainingTime;
    [Reactive] public uint SecondsRemaining { get; private set; }
    [Reactive] public bool HasError { get; private set; }

    [Reactive]
    public VerificationCountdownUpdate.Types.CountdownUpdateStatus CurrentStatus { get; private set; } =
        VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;

    [Reactive] public bool IsMaxAttemptsReached { get; private set; }
    [Reactive] public int AutoRedirectCountdown { get; private set; }
    [Reactive] public string AutoRedirectMessage { get; private set; } = string.Empty;

    [Reactive] public bool IsUiLocked { get; private set; }
    [Reactive] public bool HasValidSession { get; private set; }

    [ObservableAsProperty] public bool IsBusy { get; }
    [ObservableAsProperty] public bool IsResending { get; }


    private IDisposable? _autoRedirectTimer;
    private CancellationTokenSource? _operationCts;
    private readonly CompositeDisposable _disposables = new();
    private volatile bool _isDisposed;

    public VerifyOtpViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString mobileNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IUiDispatcher uiDispatcher) : base(systemEventService, networkProvider,
        localizationService)
    {
        _mobileNumberIdentifier = mobileNumberIdentifier;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _localizationService = localizationService;
        _uiDispatcher = uiDispatcher;

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.CreateFromObservable(() =>
        {
            MembershipHostWindowModel hostWindow = (MembershipHostWindowModel)HostScreen;
            return hostWindow.Navigate.Execute(MembershipViewType.ConfirmSecureKey);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            (code, time) => code.Length == 6 && code.All(char.IsDigit) &&
                            time != AuthenticationConstants.ExpiredRemainingTime
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, x => x.HasValidSession)
            .Select(tuple => tuple is { Item1: 0, Item2: true })
            .Catch<bool, Exception>(ex => Observable.Return(false));
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode, canResend);

        SendVerificationCodeCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        ResendSendVerificationCodeCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsResending)
            .DisposeWith(_disposables);

        this.WhenActivated(disposables =>
        {
            OnViewLoaded().Subscribe().DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.ErrorMessage)
                .DistinctUntilChanged()
                .Subscribe(err
                    =>
                {
                    HasError = !string.IsNullOrEmpty(err);
                    if (!string.IsNullOrEmpty(err) && HostScreen is MembershipHostWindowModel hostWindow)
                        ShowServerErrorNotification(hostWindow, err);
                })
                .DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.SecondsRemaining)
                .Select(FormatRemainingTime)
                .Subscribe(rt => RemainingTime = rt)
                .DisposeWith(disposables).DisposeWith(_disposables);
        });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            if (_isDisposed) return;

            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            _disposables.Add(_operationCts);

            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _mobileNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: (seconds, identifier, status, message) =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        if (_isDisposed) return;
                        try
                        {
                            HandleCountdownUpdate(seconds, identifier, status, message);
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }),
                cancellationToken: _operationCts.Token
            );

            if (result.IsErr && !_isDisposed)
            {
                ErrorMessage = result.UnwrapErr();
            }
        });
    }

    private async Task SendVerificationCode()
    {
        if (_isDisposed) return;

        string systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        if (!HasValidSession)
        {
            IsSent = false;
            HasError = true;
            ErrorMessage = _localizationService[AuthenticationConstants.NoVerificationSessionKey];
            return;
        }

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            _operationCts?.Token ?? CancellationToken.None);

        Result<Membership, string> result =
            await _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId);

        if (_isDisposed) return;

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

            if (HasValidSession)
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value)
                    .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, combinedCts.Token);
            }

            if (!_isDisposed)
                NavToPasswordConfirmation.Execute().Subscribe().DisposeWith(_disposables);
        }
        else
        {
            if (!_isDisposed)
            {
                ErrorMessage = result.UnwrapErr();
                IsSent = false;
            }
        }
    }

    private Task ReSendVerificationCode()
    {
        if (_isDisposed) return Task.CompletedTask;

        if (HasValidSession)
        {
            ErrorMessage = string.Empty;
            HasError = false;

            string deviceIdentifier = SystemDeviceIdentifier();

            CancellationTokenSource? oldCts = _operationCts;
            _operationCts = new CancellationTokenSource();
            _disposables.Add(_operationCts);

            oldCts?.Dispose();

            Task.Run(async () =>
            {
                if (_isDisposed || _operationCts.Token.IsCancellationRequested)
                    return;

                _operationCts.Token.ThrowIfCancellationRequested();

                Result<Ecliptix.Utilities.Unit, string> result =
                    await _registrationService.ResendOtpVerificationAsync(
                        VerificationSessionIdentifier!.Value,
                        _mobileNumberIdentifier,
                        deviceIdentifier,
                        onCountdownUpdate: (seconds, identifier, status, message) =>
                            RxApp.MainThreadScheduler.Schedule(() =>
                            {
                                if (!_isDisposed)
                                    HandleCountdownUpdate(seconds, identifier, status, message);
                            }),
                        cancellationToken: _operationCts.Token);

                if (result.IsErr && !_isDisposed)
                {
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        if (!_isDisposed)
                        {
                            ErrorMessage = result.UnwrapErr();
                            HasError = true;
                            SecondsRemaining = 0;
                        }
                    });
                }
            }, _operationCts.Token);
        }
        else
        {
            SecondsRemaining = 0;
            ErrorMessage = _localizationService[AuthenticationConstants.NoActiveVerificationSessionKey];
            HasError = true;
            HasValidSession = false;
        }

        return Task.CompletedTask;
    }

    private uint HandleMaxAttemptsStatus()
    {
        IsMaxAttemptsReached = true;
        StartAutoRedirect(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleNotFoundStatus()
    {
        StartAutoRedirect(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleFailedStatus(string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            ErrorMessage = error;
            HasError = true;
        }

        StartAutoRedirect(5, MembershipViewType.Welcome, ErrorMessage);

        HasValidSession = false;
        return 0;
    }

    private uint HandleUnavailable()
    {
        ErrorMessage = "";
        SecondsRemaining = 0;
        return 0;
    }

    private async void StartAutoRedirect(int seconds, MembershipViewType targetView, string localizaedMessage = "")
    {
        if (_isDisposed) return;

        await _uiDispatcher.PostAsync(() =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            AutoRedirectCountdown = seconds;
            IsUiLocked = true;
            return Task.CompletedTask;
        });

        string message = "";

        if (!string.IsNullOrEmpty(localizaedMessage))
        {
            message = localizaedMessage;
        }
        else
        {
            string key = IsMaxAttemptsReached
                ? AuthenticationConstants.MaxAttemptsReachedKey
                : AuthenticationConstants.SessionNotFoundKey;

            message = _localizationService.GetString(key);
        }

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            ShowRedirectNotification(hostWindow, message, seconds, () =>
            {
                if (!_isDisposed)
                    CleanupAndNavigate(targetView);
            });
        }    
        
    }
    

    private void HandleCountdownUpdate(uint seconds, Guid identifier,
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status, string? message)
    {
        if (_isDisposed) return;

        if (identifier != Guid.Empty)
        {
            lock (_sessionLock)
            {
                if (_isDisposed) return;

                if (_verificationSessionIdentifier == Guid.Empty)
                {
                    _verificationSessionIdentifier = identifier;
                    HasValidSession = true;
                }
                else if (_verificationSessionIdentifier != identifier)
                {
                    return;
                }
            }
        }

        if (_isDisposed) return;

        SecondsRemaining = status switch
        {
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(message),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => HandleMaxAttemptsStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.SessionExpired => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable => HandleUnavailable(),
            _ => Math.Min(seconds, SecondsRemaining)
        };
        CurrentStatus = status;
    }

    private async void CleanupAndNavigate(MembershipViewType targetView)
    {
        if (_isDisposed) return;

        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;

        await _uiDispatcher.PostAsync(() =>
        {
            if (!_isDisposed && HostScreen is MembershipHostWindowModel membershipHostWindow)
            {
                CleanupAndNavigate(membershipHostWindow, targetView);
            }

            return Task.CompletedTask;
        });
    }

    private static string FormatRemainingTime(uint seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");

    public async void HandleEnterKeyPress()
    {
        if (_isDisposed) return;

        if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
        {
            SendVerificationCodeCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    private async Task CleanupSessionAsync()
    {
        if (HasValidSession)
        {
            Guid sessionId = VerificationSessionIdentifier!.Value;
            await _registrationService.CleanupVerificationSessionAsync(sessionId);
        }
    }

    private async Task ResetUiState()
    {
        if (_isDisposed) return;

        await _uiDispatcher.PostAsync(async () =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            IsUiLocked = false;

            if (HostScreen is MembershipHostWindowModel hostWindow)
            {
                await hostWindow.HideBottomSheetAsync();
            }

            VerificationCode = "";
            IsSent = false;
            ErrorMessage = string.Empty;
            HasError = false;
            SecondsRemaining = 0;
            RemainingTime = AuthenticationConstants.InitialRemainingTime;
            IsMaxAttemptsReached = false;
            AutoRedirectMessage = string.Empty;
            AutoRedirectCountdown = 0;
            CurrentStatus = VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;
            lock (_sessionLock)
            {
                _verificationSessionIdentifier = Guid.Empty;
                HasValidSession = false;
            }
        });
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        _operationCts?.Cancel();

        _ = Task.Run(async () =>
        {
            await ResetUiState();
            await CleanupSessionAsync();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            _isDisposed = true;
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _autoRedirectTimer?.Dispose();
            _disposables?.Dispose();
        }

        base.Dispose(disposing);
    }
}