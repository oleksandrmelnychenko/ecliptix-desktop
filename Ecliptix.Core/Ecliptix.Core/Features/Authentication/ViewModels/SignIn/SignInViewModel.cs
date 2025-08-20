using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services;
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

namespace Ecliptix.Core.Features.Authentication.ViewModels.SignIn;

public sealed class SignInViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IAuthenticationService _authService;
    private readonly INetworkEvents _networkEvents;
    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<string> _signInErrorSubject = new();
    private bool _hasMobileNumberBeenTouched;
    private bool _hasSecureKeyBeenTouched;
    private bool _isDisposed;

    public string UrlPathSegment => "/sign-in";
    public IScreen HostScreen { get; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;
    [ObservableAsProperty] public bool IsBusy { get; }
    [ObservableAsProperty] public bool IsInNetworkOutage { get; }

    [ObservableAsProperty] public string? MobileNumberError { get; private set; }
    [ObservableAsProperty] public bool HasMobileNumberError { get; private set; }
    [ObservableAsProperty] public string? SecureKeyError { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyError { get; private set; }
    [ObservableAsProperty] public string? ServerError { get; private set; }
    [ObservableAsProperty] public bool HasServerError { get; private set; }

    public int CurrentSecureKeyLength => _secureKeyBuffer.Length;

    public ReactiveCommand<SystemU, Result<byte[], string>>? SignInCommand { get; private set; }
    public ReactiveCommand<SystemU, SystemU>? AccountRecoveryCommand { get; private set; }

    public SignInViewModel(
        ISystemEvents systemEvents,
        INetworkEvents networkEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authService,
        IScreen hostScreen) : base(systemEvents, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _authService = authService;
        _networkEvents = networkEvents;

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
        IObservable<string> mobileValidation = this.WhenAnyValue(x => x.MobileNumber)
            .Select(mobile => MobileNumberValidator.Validate(mobile, LocalizationService))
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

        mobileErrorStream.ToPropertyEx(this, x => x.MobileNumberError);
        this.WhenAnyValue(x => x.MobileNumberError)
            .Select(e => !string.IsNullOrEmpty(e))
            .ToPropertyEx(this, x => x.HasMobileNumberError);

        IObservable<string> secureKeyValidation = this.WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => ValidateSecureKey())
            .Replay(1)
            .RefCount();

        IObservable<string> keyDisplayErrorStream = secureKeyValidation
            .Select(error => _hasSecureKeyBeenTouched ? error : string.Empty)
            .Replay(1)
            .RefCount();

        keyDisplayErrorStream.ToPropertyEx(this, x => x.SecureKeyError);
        _signInErrorSubject
            .ToPropertyEx(this, x => x.ServerError);
        this.WhenAnyValue(x => x.ServerError)
            .Select(e => !string.IsNullOrEmpty(e))
            .ToPropertyEx(this, x => x.HasServerError);

        this.WhenAnyValue(x => x.SecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .ToPropertyEx(this, x => x.HasSecureKeyError);

        IObservable<bool> isMobileLogicallyValid = mobileValidation
            .Select(string.IsNullOrEmpty);

        IObservable<bool> isKeyLogicallyValid = secureKeyValidation
            .Select(string.IsNullOrEmpty);

        return isMobileLogicallyValid.CombineLatest(isKeyLogicallyValid,
            (isMobileValid, isKeyValid) => isMobileValid && isKeyValid
        ).DistinctUntilChanged();
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> networkStatusStream = _networkEvents.NetworkStatusChanged
            .Select(e => e.State switch
            {
                NetworkStatus.DataCenterDisconnected => true,
                NetworkStatus.ServerShutdown => true,
                NetworkStatus.ConnectionRecovering => true,
                _ => false
            })
            .StartWith(false)
            .DistinctUntilChanged();

        networkStatusStream.ToPropertyEx(this, x => x.IsInNetworkOutage);

        IObservable<bool>? canSignIn = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid);

        SignInCommand = ReactiveCommand.CreateFromTask(
            () => _authService.SignInAsync(MobileNumber, _secureKeyBuffer, ComputeConnectId()),
            canSignIn);

        SignInCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);

        AccountRecoveryCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.AccountRecovery);
        });
    }

    private void SetupSubscriptions()
    {
        SignInCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                _hasSecureKeyBeenTouched = true;
                _signInErrorSubject.OnNext(error);
            })
            .DisposeWith(_disposables);

        SignInCommand
            .Where(result => result.IsOk)
            .Subscribe(result =>
            {
                _signInErrorSubject.OnNext(string.Empty);
                byte[] sessionKey = result.Unwrap();

                Array.Clear(sessionKey, 0, sessionKey.Length);
            })
            .DisposeWith(_disposables);
    }

    private string ValidateSecureKey()
    {
        string? error = null;
        _secureKeyBuffer.WithSecureBytes(bytes =>
        {
            // Validate using secure bytes without creating strings
            error = ValidateSecureKeyBytes(bytes);
        });
        return error ?? string.Empty;
    }

    private string ValidateSecureKeyBytes(ReadOnlySpan<byte> passwordBytes)
    {
        // Basic validation without converting to string
        if (passwordBytes.Length == 0)
        {
            return LocalizationService["ValidationErrors.SecureKey.Required"];
        }

        // For sign-in, we only check if password is provided
        // Actual validation happens server-side via OPAQUE protocol
        return string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            // Clear all reactive command state
            SignInCommand?.Dispose();
            AccountRecoveryCommand?.Dispose();
            
            // Clear sensitive data
            _secureKeyBuffer?.Dispose();
            _signInErrorSubject?.Dispose();
            _disposables?.Dispose();
            
            // Clear UI state
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
            SignInCommand.Execute().Subscribe();
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;
        
        MobileNumber = string.Empty;
        _secureKeyBuffer?.Remove(0, _secureKeyBuffer.Length);
        _hasMobileNumberBeenTouched = false;
        _hasSecureKeyBeenTouched = false;
        _signInErrorSubject?.OnNext(string.Empty);
        
        // Force garbage collection of any remaining string references
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}