using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class SecureKeyVerifierViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly SecureTextBuffer _verifySecureKeyBuffer = new();
    private bool _hasSecureKeyBeenTouched;
    private bool _hasVerifySecureKeyBeenTouched;

    public int CurrentSecureKeyLength => _secureKeyBuffer.Length;
    public int CurrentVerifySecureKeyLength => _verifySecureKeyBuffer.Length;

    [Reactive] public string? SecureKeyError { get; set; }
    [Reactive] public bool HasSecureKeyError { get; set; }
    [Reactive] public string? VerifySecureKeyError { get; set; }
    [Reactive] public bool HasVerifySecureKeyError { get; set; }
    [Reactive] public string? ServerError { get; set; }
    [Reactive] public bool HasServerError { get; set; }

    [ObservableAsProperty] public PasswordStrength CurrentSecureKeyStrength { get; private set; }
    [ObservableAsProperty] public string? SecureKeyStrengthMessage { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyBeenTouched { get; private set; }

    [Reactive] public bool CanSubmit { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<SystemU, SystemU> SubmitCommand { get; }
    public ReactiveCommand<SystemU, SystemU> NavPassConfToPassPhase { get; }

    private ByteString? VerificationSessionId { get; set; }

    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IAuthenticationService _authenticationService;

    public SecureKeyVerifierViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IAuthenticationService authenticationService
    ) : base(systemEventService, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _authenticationService = authenticationService;

        IObservable<bool> isFormLogicallyValid = SetupValidation();

        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitRegistrationSecureKeyAsync, canExecuteSubmit);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
        canExecuteSubmit.BindTo(this, x => x.CanSubmit);

        NavPassConfToPassPhase = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.PassPhase);
        });

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.CanSubmit).BindTo(this, x => x.CanSubmit).DisposeWith(disposables);

            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe(result =>
                {
                    if (!result.IsErr) return;
                    ((MembershipHostWindowModel)HostScreen).ClearNavigationStack();
                    ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.Welcome);
                })
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.ServerError)
                .DistinctUntilChanged()
                .Subscribe(err
                    =>
                {
                    HasServerError = !string.IsNullOrEmpty(err);
                    if (!string.IsNullOrEmpty(err) && HostScreen is MembershipHostWindowModel hostWindow)
                        ShowServerErrorNotification(hostWindow, err);
                })
                .DisposeWith(disposables);

            SubmitCommand
                .Where(_ => !IsBusy && CanSubmit)
                .Subscribe(_ =>
                {
                    ((MembershipHostWindowModel)HostScreen).ClearNavigationStack();
                    ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.PassPhase);
                })
                .DisposeWith(disposables);
        });
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
        VerificationSessionId = settings.Membership.UniqueIdentifier;
        return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
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

    public void InsertVerifySecureKeyChars(int index, string chars)
    {
        if (!_hasVerifySecureKeyBeenTouched) _hasVerifySecureKeyBeenTouched = true;
        _verifySecureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    public void RemoveVerifySecureKeyChars(int index, int count)
    {
        if (!_hasVerifySecureKeyBeenTouched) _hasVerifySecureKeyBeenTouched = true;
        _verifySecureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<SystemU> languageTrigger = LanguageChanged;

        IObservable<SystemU> lengthTrigger = this
            .WhenAnyValue(x => x.CurrentSecureKeyLength, x => x.CurrentVerifySecureKeyLength)
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<(string? Error, string Recommendations, PasswordStrength Strength)> secureKeyValidation =
            validationTrigger
                .Select(_ => ValidateSecureKeyWithStrength())
                .Replay(1)
                .RefCount();

        secureKeyValidation.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentSecureKeyStrength);
        secureKeyValidation.Select(v =>
                _hasSecureKeyBeenTouched
                    ? FormatSecureKeyStrengthMessage(v.Strength, v.Error, v.Recommendations)
                    : string.Empty)
            .ToPropertyEx(this, x => x.SecureKeyStrengthMessage);

        this.WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => _hasSecureKeyBeenTouched)
            .ToPropertyEx(this, x => x.HasSecureKeyBeenTouched);

        IObservable<string> secureKeyErrorStream = secureKeyValidation
            .Select(v =>
                _hasSecureKeyBeenTouched
                    ? FormatSecureKeyStrengthMessage(v.Strength, v.Error, v.Recommendations)
                    : string.Empty)
            .Replay(1)
            .RefCount();

        secureKeyErrorStream
            .Subscribe(error => SecureKeyError = error);
        this.WhenAnyValue(x => x.SecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasSecureKeyError = flag);

        IObservable<bool> isSecureKeyLogicallyValid = secureKeyValidation.Select(v => string.IsNullOrEmpty(v.Error));

        IObservable<SystemU> verifyValidationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<bool> secureKeysMatch = verifyValidationTrigger
            .Select(_ => DoSecureKeysMatch())
            .Replay(1)
            .RefCount();

        IObservable<string> verifySecureKeyErrorStream = secureKeysMatch
            .Select(match =>
            {
                bool shouldShowError = _hasVerifySecureKeyBeenTouched && !match;

                return shouldShowError
                    ? LocalizationService[AuthenticationConstants.VerifySecureKeyDoesNotMatchKey]
                    : string.Empty;
            })
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();

        verifySecureKeyErrorStream
            .Subscribe(error => VerifySecureKeyError = error);

        this.WhenAnyValue(x => x.VerifySecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasVerifySecureKeyError = flag);

        return isSecureKeyLogicallyValid
            .CombineLatest(secureKeysMatch, (isSecureKeyValid, areMatching) => isSecureKeyValid && areMatching)
            .DistinctUntilChanged();
    }

    private (string? Error, string Recommendations, PasswordStrength Strength) ValidateSecureKeyWithStrength()
    {
        string? error = null;
        string recommendations = string.Empty;
        PasswordStrength strength = PasswordStrength.Invalid;

        _secureKeyBuffer.WithSecureBytes(bytes =>
        {
            string secureKey = Encoding.UTF8.GetString(bytes);
            (error, List<string> recs) = SecureKeyValidator.Validate(secureKey, LocalizationService);
            strength = SecureKeyValidator.EstimatePasswordStrength(secureKey, LocalizationService);
            if (recs.Count != 0)
            {
                recommendations = recs.First();
            }
        });
        return (error, recommendations, strength);
    }

    private bool DoSecureKeysMatch()
    {
        if (_secureKeyBuffer.Length != _verifySecureKeyBuffer.Length)
        {
            return false;
        }

        if (_secureKeyBuffer.Length == 0)
        {
            return true;
        }

        byte[] secureKeyArray = new byte[_secureKeyBuffer.Length];
        byte[] verifyArray = new byte[_verifySecureKeyBuffer.Length];

        _secureKeyBuffer.WithSecureBytes(secureKeyBytes => { secureKeyBytes.CopyTo(secureKeyArray.AsSpan()); });

        _verifySecureKeyBuffer.WithSecureBytes(verifyBytes => { verifyBytes.CopyTo(verifyArray.AsSpan()); });

        return secureKeyArray.AsSpan().SequenceEqual(verifyArray);
    }

    private string FormatSecureKeyStrengthMessage(PasswordStrength strength, string? error, string recommendations)
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
        return string.IsNullOrEmpty(message) ? strengthText : $"{strengthText}: {message}";
    }

    private async Task SubmitRegistrationSecureKeyAsync()
    {
        if (IsBusy || !CanSubmit) return;

        if (VerificationSessionId == null)
        {
            ServerError = LocalizationService[AuthenticationConstants.NoVerificationSessionKey];
            HasServerError = true;
            return;
        }

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Unit, string> registrationResult = await _registrationService.CompleteRegistrationAsync(
            VerificationSessionId,
            _secureKeyBuffer,
            connectId);

        if (registrationResult.IsErr)
        {
            ServerError = registrationResult.UnwrapErr();
            HasServerError = true;
        }
        else
        {
            MembershipHostWindowModel hostViewModel = (MembershipHostWindowModel)HostScreen;
            string registrationMobileNumber = hostViewModel.RegistrationMobileNumber!;

            Result<Unit, string> signInResult = await _authenticationService.SignInAsync(
                registrationMobileNumber,
                _secureKeyBuffer,
                connectId);

            if (signInResult.IsOk)
            {
                try
                {
                    await hostViewModel.SwitchToMainWindowCommand.Execute();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to switch to main window after successful sign-in");
                    ServerError = $"Failed to navigate to main window: {ex.Message}";
                    HasServerError = true;
                }
            }
            else
            {
                ServerError = $"Auto-login failed: {signInResult.UnwrapErr()}";
                HasServerError = true;
            }
        }
    }

    public async void HandleEnterKeyPress()
    {
        if (SubmitCommand != null && await SubmitCommand.CanExecute.FirstOrDefaultAsync())
        {
            SubmitCommand.Execute().Subscribe();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _secureKeyBuffer.Dispose();
            _verifySecureKeyBuffer.Dispose();
        }

        base.Dispose(disposing);
    }


    public void ResetState()
    {
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);
        _verifySecureKeyBuffer.Remove(0, _verifySecureKeyBuffer.Length);
        _hasSecureKeyBeenTouched = false;
        _hasVerifySecureKeyBeenTouched = false;

        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        VerifySecureKeyError = string.Empty;
        HasVerifySecureKeyError = false;

        ServerError = string.Empty;
        HasServerError = false;
    }

    public string? UrlPathSegment { get; } = "/secure-key-confirmation";
    public IScreen HostScreen { get; }
}