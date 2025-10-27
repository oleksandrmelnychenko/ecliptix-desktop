using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.PasswordRecovery;

public sealed class ForgotPasswordResetViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly SecureTextBuffer _newPasswordBuffer = new();
    private readonly SecureTextBuffer _confirmPasswordBuffer = new();
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IPasswordRecoveryService _passwordRecoveryService;
    private readonly IAuthenticationService _authenticationService;
    private readonly CompositeDisposable _disposables = new();

    private bool _hasNewPasswordBeenTouched;
    private bool _hasConfirmPasswordBeenTouched;
    private CancellationTokenSource? _currentOperationCts;

    public ForgotPasswordResetViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IPasswordRecoveryService passwordRecoveryService,
        IAuthenticationService authenticationService
    ) : base(networkProvider, localizationService, connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _passwordRecoveryService = passwordRecoveryService;
        _authenticationService = authenticationService;

        IObservable<bool> isFormLogicallyValid = SetupValidation();

        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitPasswordResetAsync, canExecuteSubmit);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy).DisposeWith(_disposables);
        canExecuteSubmit.BindTo(this, x => x.CanSubmit).DisposeWith(_disposables);

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.CanSubmit).BindTo(this, x => x.CanSubmit).DisposeWith(disposables);

            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe(result =>
                {
                    if (!result.IsErr)
                    {
                        return;
                    }

                    ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                    ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.Welcome);
                })
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.ServerError)
                .DistinctUntilChanged()
                .Subscribe(err =>
                {
                    HasServerError = !string.IsNullOrEmpty(err);
                    if (!string.IsNullOrEmpty(err) && HostScreen is AuthenticationViewModel hostWindow)
                    {
                        ShowServerErrorNotification(hostWindow, err);
                    }
                })
                .DisposeWith(disposables);

            SubmitCommand
                .Where(_ => !IsBusy && CanSubmit)
                .Subscribe(_ =>
                {
                    if (HasServerError)
                    {
                        return;
                    }

                    ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                    ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.SignIn);
                })
                .DisposeWith(disposables);
        });
    }

    public string? UrlPathSegment { get; } = "/forgot-password-reset";
    public IScreen HostScreen { get; }

    public int CurrentNewPasswordLength => _newPasswordBuffer.Length;
    public int CurrentConfirmPasswordLength => _confirmPasswordBuffer.Length;

    public ReactiveCommand<SystemU, SystemU> SubmitCommand { get; }

    [Reactive] public string? NewPasswordError { get; set; }
    [Reactive] public bool HasNewPasswordError { get; set; }
    [Reactive] public string? ConfirmPasswordError { get; set; }
    [Reactive] public bool HasConfirmPasswordError { get; set; }
    [Reactive] public string? ServerError { get; set; }
    [Reactive] public bool HasServerError { get; set; }
    [Reactive] public bool CanSubmit { get; private set; }

    [ObservableAsProperty] public PasswordStrength CurrentPasswordStrength { get; private set; }
    [ObservableAsProperty] public string? PasswordStrengthMessage { get; private set; }
    [ObservableAsProperty] public bool HasNewPasswordBeenTouched { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    private ByteString? MembershipIdentifier { get; set; }

    public void InsertNewPasswordChars(int index, string chars)
    {
        if (!_hasNewPasswordBeenTouched)
        {
            _hasNewPasswordBeenTouched = true;
        }

        _newPasswordBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentNewPasswordLength));
    }

    public void RemoveNewPasswordChars(int index, int count)
    {
        if (!_hasNewPasswordBeenTouched)
        {
            _hasNewPasswordBeenTouched = true;
        }

        _newPasswordBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentNewPasswordLength));
    }

    public void InsertConfirmPasswordChars(int index, string chars)
    {
        if (!_hasConfirmPasswordBeenTouched)
        {
            _hasConfirmPasswordBeenTouched = true;
        }

        _confirmPasswordBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentConfirmPasswordLength));
    }

    public void RemoveConfirmPasswordChars(int index, int count)
    {
        if (!_hasConfirmPasswordBeenTouched)
        {
            _hasConfirmPasswordBeenTouched = true;
        }

        _confirmPasswordBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentConfirmPasswordLength));
    }

    public async Task HandleEnterKeyPressAsync()
    {
        try
        {
            if (await SubmitCommand.CanExecute.FirstOrDefaultAsync())
            {
                SubmitCommand.Execute().Subscribe().DisposeWith(_disposables);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FORGOT-PASSWORD-ENTERKEY] Error handling enter key press");
        }
    }

    public void ResetState()
    {
        CancelCurrentOperation();

        _newPasswordBuffer.Remove(0, _newPasswordBuffer.Length);
        _confirmPasswordBuffer.Remove(0, _confirmPasswordBuffer.Length);
        _hasNewPasswordBeenTouched = false;
        _hasConfirmPasswordBeenTouched = false;

        NewPasswordError = string.Empty;
        HasNewPasswordError = false;
        ConfirmPasswordError = string.Empty;
        HasConfirmPasswordError = false;

        ServerError = string.Empty;
        HasServerError = false;
    }

    private async Task<Result<Unit, InternalServiceApiFailure>> LoadMembershipAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> applicationInstance =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (applicationInstance.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(applicationInstance.UnwrapErr());
        }

        ApplicationInstanceSettings settings = applicationInstance.Unwrap();
        MembershipIdentifier = settings.Membership.UniqueIdentifier;
        return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<SystemU> languageTrigger = LanguageChanged;

        IObservable<SystemU> lengthTrigger = this
            .WhenAnyValue(x => x.CurrentNewPasswordLength, x => x.CurrentConfirmPasswordLength)
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<(string? Error, string Recommendations, PasswordStrength Strength)> passwordValidation =
            validationTrigger
                .Select(_ => ValidatePasswordWithStrength())
                .Replay(1)
                .RefCount();

        passwordValidation.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentPasswordStrength).DisposeWith(_disposables);
        passwordValidation.Select(v =>
                _hasNewPasswordBeenTouched
                    ? FormatPasswordStrengthMessage(v.Strength, v.Error, v.Recommendations)
                    : string.Empty)
            .ToPropertyEx(this, x => x.PasswordStrengthMessage).DisposeWith(_disposables);

        this.WhenAnyValue(x => x.CurrentNewPasswordLength)
            .Select(_ => _hasNewPasswordBeenTouched)
            .ToPropertyEx(this, x => x.HasNewPasswordBeenTouched).DisposeWith(_disposables);

        IObservable<string> passwordErrorStream = passwordValidation
            .Select(v =>
                _hasNewPasswordBeenTouched
                    ? FormatPasswordStrengthMessage(v.Strength, v.Error, v.Recommendations)
                    : string.Empty)
            .Replay(1)
            .RefCount();

        passwordErrorStream
            .Subscribe(error => NewPasswordError = error)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.NewPasswordError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasNewPasswordError = flag)
            .DisposeWith(_disposables);

        IObservable<bool> isPasswordLogicallyValid = passwordValidation.Select(v => string.IsNullOrEmpty(v.Error));

        IObservable<SystemU> confirmValidationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<bool> passwordsMatch = confirmValidationTrigger
            .Select(_ => DoPasswordsMatch())
            .Replay(1)
            .RefCount();

        IObservable<string> confirmPasswordErrorStream = passwordsMatch
            .Select(match =>
            {
                bool shouldShowError = _hasConfirmPasswordBeenTouched && !match;

                return shouldShowError
                    ? LocalizationService[AuthenticationConstants.VerifySecureKeyDoesNotMatchKey]
                    : string.Empty;
            })
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(150))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        confirmPasswordErrorStream
            .DistinctUntilChanged()
            .Subscribe(error => ConfirmPasswordError = error)
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ConfirmPasswordError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasConfirmPasswordError = flag)
            .DisposeWith(_disposables);

        return isPasswordLogicallyValid
            .CombineLatest(passwordsMatch, (isPasswordValid, areMatching) => isPasswordValid && areMatching)
            .DistinctUntilChanged();
    }

    private (string? Error, string Recommendations, PasswordStrength Strength) ValidatePasswordWithStrength()
    {
        string? error = null;
        string recommendations = string.Empty;
        PasswordStrength strength = PasswordStrength.Invalid;

        _newPasswordBuffer.WithSecureBytes(bytes =>
        {
            string password = Encoding.UTF8.GetString(bytes);
            (error, System.Collections.Generic.List<string> recs) = SecureKeyValidator.Validate(password, LocalizationService);
            strength = SecureKeyValidator.EstimatePasswordStrength(password, LocalizationService);
            if (recs.Count > 0)
            {
                recommendations = recs[0];
            }
        });
        return (error, recommendations, strength);
    }

    private bool DoPasswordsMatch()
    {
        if (_newPasswordBuffer.Length != _confirmPasswordBuffer.Length)
        {
            return false;
        }

        if (_newPasswordBuffer.Length == 0)
        {
            return true;
        }

        int length = _newPasswordBuffer.Length;
        byte[] newPasswordArray = ArrayPool<byte>.Shared.Rent(length);
        byte[] confirmArray = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            _newPasswordBuffer.WithSecureBytes(secureKeyBytes => { secureKeyBytes.CopyTo(newPasswordArray.AsSpan()); });
            _confirmPasswordBuffer.WithSecureBytes(verifyBytes => { verifyBytes.CopyTo(confirmArray.AsSpan()); });

            return CryptographicOperations.FixedTimeEquals(
                newPasswordArray.AsSpan(0, length),
                confirmArray.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(newPasswordArray, clearArray: true);
            ArrayPool<byte>.Shared.Return(confirmArray, clearArray: true);
        }
    }

    private string FormatPasswordStrengthMessage(PasswordStrength strength, string? error, string recommendations)
    {
        string strengthText = strength switch
        {
            PasswordStrength.Invalid => LocalizationService[AuthenticationConstants.PasswordStrengthInvalidKey],
            PasswordStrength.VeryWeak => LocalizationService[AuthenticationConstants.PasswordStrengthVeryWeakKey],
            PasswordStrength.Weak => LocalizationService[AuthenticationConstants.PasswordStrengthWeakKey],
            PasswordStrength.Good => LocalizationService[AuthenticationConstants.PasswordStrengthGoodKey],
            PasswordStrength.Strong => LocalizationService[AuthenticationConstants.PasswordStrengthStrongKey],
            PasswordStrength.VeryStrong => LocalizationService[AuthenticationConstants.PasswordStrengthVeryStrongKey],
            _ => LocalizationService[AuthenticationConstants.PasswordStrengthInvalidKey]
        };

        string message = !string.IsNullOrEmpty(error) ? error : recommendations;
        return string.IsNullOrEmpty(message) ? strengthText : string.Concat(strengthText, ": ", message);
    }

    private async Task<SystemU> SubmitPasswordResetAsync()
    {
        if (IsBusy || !CanSubmit)
        {
            return SystemU.Default;
        }

        if (MembershipIdentifier == null)
        {
            ServerError = LocalizationService[AuthenticationConstants.NoVerificationSessionKey];
            HasServerError = true;
            return SystemU.Default;
        }

        ServerError = string.Empty;
        HasServerError = false;

        try
        {
            CancellationTokenSource operationCts = RecreateCancellationToken(ref _currentOperationCts);
            CancellationToken operationToken = operationCts.Token;

            try
            {
                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

                Result<Unit, string> resetResult = await _passwordRecoveryService.CompletePasswordResetAsync(
                    MembershipIdentifier,
                    _newPasswordBuffer,
                    connectId,
                    operationToken);

                if (resetResult.IsErr)
                {
                    ServerError = resetResult.UnwrapErr();
                    HasServerError = true;
                    return SystemU.Default;
                }

                AuthenticationViewModel hostViewModel = (AuthenticationViewModel)HostScreen;
                string? recoveryMobileNumber = hostViewModel.RecoveryMobileNumber;

                if (string.IsNullOrEmpty(recoveryMobileNumber))
                {
                    ServerError = "Recovery mobile number not found. Please try again.";
                    HasServerError = true;
                    return SystemU.Default;
                }

                Result<Unit, AuthenticationFailure> signInResult = await _authenticationService.SignInAsync(
                    recoveryMobileNumber,
                    _newPasswordBuffer,
                    connectId,
                    operationToken);

                if (signInResult.IsOk)
                {
                    try
                    {
                        await hostViewModel.SwitchToMainWindowCommand.Execute();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to switch to main window after successful password reset");
                        ServerError = $"Failed to navigate to main window: {ex.Message}";
                        HasServerError = true;
                    }
                }
                else
                {
                    AuthenticationFailure failure = signInResult.UnwrapErr();
                    ServerError = string.Concat("Auto-login failed: ", failure.Message);
                    HasServerError = true;
                }

                return SystemU.Default;
            }
            finally
            {
                if (ReferenceEquals(_currentOperationCts, operationCts))
                {
                    _currentOperationCts = null;
                }

                operationCts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return SystemU.Default;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelCurrentOperation();
            _newPasswordBuffer.Dispose();
            _confirmPasswordBuffer.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(ref _currentOperationCts, null);
        if (operationSource == null)
        {
            return;
        }

        try
        {
            operationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            operationSource.Dispose();
        }
    }
}
