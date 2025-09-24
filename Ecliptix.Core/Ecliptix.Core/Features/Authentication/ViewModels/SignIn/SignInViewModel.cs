using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SystemU = System.Reactive.Unit;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Protobuf.Protocol;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.ViewModels.SignIn;

public sealed class SignInViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly IAuthenticationService _authService;
    private readonly INetworkEventService _networkEventService;
    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<string> _signInErrorSubject = new();
    private readonly MembershipHostWindowModel _hostWindowModel;
    private bool _hasMobileNumberBeenTouched;
    private bool _hasSecureKeyBeenTouched;
    private bool _isDisposed;

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

    public ReactiveCommand<SystemU, Result<Unit, string>>? SignInCommand { get; private set; }
    public ReactiveCommand<SystemU, SystemU>? AccountRecoveryCommand { get; private set; }

    public SignInViewModel(
        ISystemEventService systemEventService,
        INetworkEventService networkEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authService,
        IScreen hostScreen) : base(systemEventService, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _localizationService = localizationService;
        _authService = authService;
        _networkEventService = networkEventService;
        _hostWindowModel = (MembershipHostWindowModel)hostScreen;

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
        SetupSubscriptions();
    }

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
            .Subscribe(error => SecureKeyError = error)
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasSecureKeyError = flag)
            .DisposeWith(_disposables);

        _signInErrorSubject
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
            .Do(valid => Serilog.Log.Debug("✅ Form logically valid: {Valid}", valid));

        return isFormLogicallyValid;
    }


    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> networkStatusStream = Observable.Create<bool>(observer =>
            {
                bool isInOutage = _networkEventService.CurrentStatus switch
                {
                    NetworkStatus.DataCenterDisconnected => true,
                    NetworkStatus.ServerShutdown => true,
                    NetworkStatus.ConnectionRecovering => true,
                    _ => false
                };
                observer.OnNext(isInOutage);

                return _networkEventService.OnNetworkStatusChanged(evt =>
                {
                    bool outage = evt.State switch
                    {
                        NetworkStatus.DataCenterDisconnected => true,
                        NetworkStatus.ServerShutdown => true,
                        NetworkStatus.ConnectionRecovering => true,
                        _ => false
                    };
                    observer.OnNext(outage);
                    return Task.CompletedTask;
                });
            }).DistinctUntilChanged()
            .Do(outage => Serilog.Log.Debug("🌐 Network outage status changed: {Outage}", outage));

        networkStatusStream.ToPropertyEx(this, x => x.IsInNetworkOutage);

        IObservable<bool> canSignIn = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid)
            .Do(canExecute => Serilog.Log.Debug("🔑 SignInCommand can execute: {CanExecute}", canExecute));
        ;

        SignInCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                try
                {
                    uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
                    Result<Unit, string> result =
                        await _authService.SignInAsync(MobileNumber!, _secureKeyBuffer, connectId);
                    return result;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "🔐 SignInCommand: Exception in command execution");
                    throw;
                }
            },
            canSignIn);

        SignInCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);

        AccountRecoveryCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.AccountRecovery);
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
                _signInErrorSubject.OnNext(error);
                ShowServerErrorNotification(error);
            })
            .DisposeWith(_disposables);

        SignInCommand?
            .Where(result => result.IsOk)
            .Subscribe(result =>
            {
                _signInErrorSubject.OnNext(string.Empty);

                _hostWindowModel.SwitchToMainWindowCommand.Execute().Subscribe(
                    _ => { },
                    ex => Serilog.Log.Error(ex, "Failed to transition to main window"),
                    () => Serilog.Log.Information("Main window transition completed")
                );
            })
            .DisposeWith(_disposables);
    }

    private string ValidateSecureKey()
    {
        string? error = null;
        _secureKeyBuffer.WithSecureBytes(bytes => { error = ValidateSecureKeyBytes(bytes); });
        return error ?? string.Empty;
    }

    private string ValidateSecureKeyBytes(ReadOnlySpan<byte> passwordBytes) =>
        passwordBytes.Length == 0 ? LocalizationService["ValidationErrors.SecureKey.Required"] : string.Empty;

    public new void Dispose() =>
        Dispose(true);

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
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
    
    public async void HandleEnterKeyPress()
    {
        if (SignInCommand != null && await SignInCommand.CanExecute.FirstOrDefaultAsync())
        {
            IDisposable disp = SignInCommand.Execute().Subscribe();
            _disposables.Add(disp);
        }
    }
    
    private void ShowServerErrorNotification(string errorMessage)
    {
        if (_isDisposed || string.IsNullOrEmpty(errorMessage)) return;

        UserRequestErrorViewModel errorViewModel = new(errorMessage, _localizationService);
        UserRequestErrorView errorView = new() { DataContext = errorViewModel };

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await hostWindow.ShowBottomSheet(
                        BottomSheetComponentType.UserRequestError, 
                        errorView, 
                        showScrim: false, 
                        isDismissable: true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to show server error notification");
                }
            });
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        _hasMobileNumberBeenTouched = false;
        _hasSecureKeyBeenTouched = false;

        MobileNumber = string.Empty;
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);

        _signInErrorSubject.OnNext(string.Empty);

        MobileNumberError = string.Empty;
        HasMobileNumberError = false;
        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        ServerError = string.Empty;
        HasServerError = false;
    }
}