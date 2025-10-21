using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Membership.Constants;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SystemU = System.Reactive.Unit;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Protobuf.Protocol;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Core.Features.Authentication.ViewModels.SignIn;

public sealed class SignInViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IAuthenticationService _authService;
    private readonly IConnectivityService _connectivityService;
    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<string> _signInErrorSubject = new();
    private readonly AuthenticationViewModel _hostWindowModel;

    private CancellationTokenSource? _signInCancellationTokenSource;
    private bool _hasMobileNumberBeenTouched;
    private bool _hasSecureKeyBeenTouched;
    private bool _isDisposed;

    public SignInViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authService,
        IScreen hostScreen) : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _authService = authService;
        _connectivityService = connectivityService;
        _hostWindowModel = (AuthenticationViewModel)hostScreen;

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
        SetupSubscriptions();
    }

    public string UrlPathSegment => "/sign-in";

    public IScreen HostScreen { get; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    [ObservableAsProperty] public bool IsBusy { get; }

    [ObservableAsProperty] public bool IsInNetworkOutage { get; }

    [Reactive] public string? MobileNumberError { get; set; }

    [Reactive] public bool HasMobileNumberError { get; set; }

    [Reactive] public string? SecureKeyError { get; set; }

    [Reactive] public bool HasSecureKeyError { get; set; }

    [Reactive] public string? ServerError { get; set; }

    [Reactive] public bool HasServerError { get; set; }

    public int CurrentSecureKeyLength => _secureKeyBuffer.Length;

    public ReactiveCommand<SystemU, Result<Unit, AuthenticationFailure>>? SignInCommand { get; private set; }

    public ReactiveCommand<SystemU, SystemU>? AccountRecoveryCommand { get; private set; }

    public void InsertSecureKeyChars(int index, string chars)
    {
        if (!_hasSecureKeyBeenTouched) _hasSecureKeyBeenTouched = true;

        _secureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public void RemoveSecureKeyChars(int index, int count)
    {
        if (!_hasSecureKeyBeenTouched) _hasSecureKeyBeenTouched = true;

        _secureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public async Task HandleEnterKeyPressAsync()
    {
        try
        {
            if (await SignInCommand!.CanExecute.FirstOrDefaultAsync())
            {
                SignInCommand.Execute().Subscribe().DisposeWith(_disposables);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SIGNIN-ENTERKEY] Error handling enter key press");
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        _hasMobileNumberBeenTouched = false;
        _hasSecureKeyBeenTouched = false;

        MobileNumber = string.Empty;
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);

        try
        {
            _signInCancellationTokenSource?.Cancel();
            _signInCancellationTokenSource?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _signInErrorSubject.OnNext(string.Empty);

        MobileNumberError = string.Empty;
        HasMobileNumberError = false;
        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        ServerError = string.Empty;
        HasServerError = false;
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<SystemU> languageTrigger = LanguageChanged;

        IObservable<SystemU> mobileTrigger = this
            .WhenAnyValue(x => x.MobileNumber)
            .Select(_ => SystemU.Default);

        IObservable<SystemU> secureKeyTrigger = this
            .WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger =
            mobileTrigger
                .Merge(secureKeyTrigger)
                .Merge(languageTrigger);

        IObservable<string> mobileValidation = validationTrigger
            .Select(_ => MobileNumberValidator.Validate(MobileNumber, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.MobileNumber)
            .CombineLatest(mobileValidation, (mobile, error) =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                    _hasMobileNumberBeenTouched = true;

                return !_hasMobileNumberBeenTouched ? string.Empty : error;
            })
            .Replay(1)
            .RefCount();

        mobileErrorStream
            .DistinctUntilChanged()
            .Subscribe(error => MobileNumberError = error)
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.MobileNumberError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasMobileNumberError = flag)
            .DisposeWith(_disposables);

        IObservable<string> secureKeyValidation = validationTrigger
            .Select(_ => ValidateSecureKey())
            .Replay(1)
            .RefCount();

        IObservable<string> keyDisplayErrorStream = secureKeyValidation
            .Select(error => _hasSecureKeyBeenTouched ? error : string.Empty)
            .Replay(1)
            .RefCount();

        keyDisplayErrorStream
            .DistinctUntilChanged()
            .Subscribe(error => SecureKeyError = error)
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasSecureKeyError = flag)
            .DisposeWith(_disposables);

        _signInErrorSubject
            .DistinctUntilChanged()
            .Subscribe(err => ServerError = err)
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ServerError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasServerError = flag)
            .DisposeWith(_disposables);

        IObservable<bool> isMobileLogicallyValid = mobileValidation
            .Select(string.IsNullOrEmpty);

        IObservable<bool> isKeyLogicallyValid = secureKeyValidation
            .Select(string.IsNullOrEmpty);

        IObservable<bool> isFormLogicallyValid = isMobileLogicallyValid
            .CombineLatest(isKeyLogicallyValid, (isMobileValid, isKeyValid) => isMobileValid && isKeyValid)
            .DistinctUntilChanged()
            .Do(valid =>
            {
                if (Serilog.Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("✅ Form logically valid: {Valid}", valid);
                }
            });

        return isFormLogicallyValid;
    }

    private static bool IsNetworkInOutage(ConnectivitySnapshot snapshot) =>
        snapshot.Status is ConnectivityStatus.Disconnected
            or ConnectivityStatus.ShuttingDown
            or ConnectivityStatus.Recovering
            or ConnectivityStatus.RetriesExhausted
            or ConnectivityStatus.Unavailable;

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> networkStatusStream = _connectivityService.ConnectivityStream
            .Select(IsNetworkInOutage)
            .StartWith(IsNetworkInOutage(_connectivityService.CurrentSnapshot))
            .DistinctUntilChanged();

        networkStatusStream.ToPropertyEx(this, x => x.IsInNetworkOutage);

        IObservable<bool> canSignIn = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid);

        SignInCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                _signInCancellationTokenSource?.Cancel();
                _signInCancellationTokenSource?.Dispose();
                _signInCancellationTokenSource = new CancellationTokenSource();

                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
                CancellationToken cancellationToken = _signInCancellationTokenSource.Token;
                Result<Unit, AuthenticationFailure> result =
                    await _authService.SignInAsync(MobileNumber, _secureKeyBuffer, connectId, cancellationToken);
                return result;
            },
            canSignIn);

        SignInCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);

        AccountRecoveryCommand = ReactiveCommand.Create(() =>
        {
            ((AuthenticationViewModel)HostScreen).StartPasswordRecoveryFlow();
        });
    }

    private void SetupSubscriptions()
    {
        SignInCommand?
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                _hasSecureKeyBeenTouched = true;
                _signInErrorSubject.OnNext(error.Message);
                if (HostScreen is AuthenticationViewModel hostWindow)
                    ShowServerErrorNotification(hostWindow, error.Message);
            })
            .DisposeWith(_disposables);

        SignInCommand?
            .Where(result => result.IsOk)
            .Subscribe(result =>
            {
                _signInErrorSubject.OnNext(string.Empty);

                _hostWindowModel.SwitchToMainWindowCommand.Execute().Subscribe(
                    _ => { },
                    ex => { Log.Error(ex, "Failed to transition to main window"); },
                    () => Log.Information("Main window transition completed")
                );
            })
            .DisposeWith(_disposables);
    }

    private string ValidateSecureKey() =>
        _secureKeyBuffer.Length == 0
            ? LocalizationService[SecureKeyValidatorConstants.LocalizationKeys.Required]
            : string.Empty;

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            try
            {
                _signInCancellationTokenSource?.Cancel();
                _signInCancellationTokenSource?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            _signInCancellationTokenSource = null;

            SignInCommand?.Dispose();
            AccountRecoveryCommand?.Dispose();

            _secureKeyBuffer.Dispose();
            _signInErrorSubject.Dispose();
            _disposables.Dispose();

            MobileNumber = string.Empty;
            _hasMobileNumberBeenTouched = false;
            _hasSecureKeyBeenTouched = false;
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    public new void Dispose() =>
        Dispose(true);
}
