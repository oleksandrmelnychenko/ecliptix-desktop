using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Org.BouncyCastle.Crypto.Parameters;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Org.BouncyCastle.Math;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class SecureKeyVerifierViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly SecureTextBuffer _verifySecureKeyBuffer = new();
    private bool _hasSecureKeyBeenTouched;
    private bool _hasVerifySecureKeyBeenTouched;

    public int CurrentSecureKeyLength => _secureKeyBuffer.Length;
    public int CurrentVerifySecureKeyLength => _verifySecureKeyBuffer.Length;

    [ObservableAsProperty] public string? SecureKeyError { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyError { get; private set; }
    [ObservableAsProperty] public string? VerifySecureKeyError { get; private set; }
    [ObservableAsProperty] public bool HasVerifySecureKeyError { get; private set; }

    [ObservableAsProperty] public PasswordStrength CurrentSecureKeyStrength { get; private set; }
    [ObservableAsProperty] public string? SecureKeyStrengthMessage { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyBeenTouched { get; private set; }

    [Reactive] public bool CanSubmit { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SubmitCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavPassConfToPassPhase { get; }

    private ByteString? VerificationSessionId { get; set; }

    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;

    public SecureKeyVerifierViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider
    ) : base(systemEventService, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        IObservable<bool> isFormLogicallyValid = SetupValidation();

        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitRegistrationSecureKeyAsync);
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
                    if (result.IsErr)
                    {
                        Log.Debug("Failed to load membership settings: {Error}", result.UnwrapErr().Message);
                        ((MembershipHostWindowModel)HostScreen).ClearNavigationStack();
                        ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.Welcome);
                    }
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
    IObservable<System.Reactive.Unit> languageTrigger = 
        Observable.FromEvent(
                    handler => LocalizationService.LanguageChanged += handler,
                    handler => LocalizationService.LanguageChanged -= handler)
                .Select(_ => System.Reactive.Unit.Default);

    IObservable<System.Reactive.Unit> lengthTrigger = this
        .WhenAnyValue(x => x.CurrentSecureKeyLength)
        .Select(_ => System.Reactive.Unit.Default);
    
    IObservable<System.Reactive.Unit> validationTrigger = lengthTrigger.Merge(languageTrigger);
    
    IObservable<(string? Error, string Recommendations, PasswordStrength Strength)> secureKeyValidation = validationTrigger
        .Select(_ => ValidateSecureKeyWithStrength())
        .Replay(1)
        .RefCount();
    
    secureKeyValidation.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentSecureKeyStrength);
    secureKeyValidation.Select(v => _hasSecureKeyBeenTouched ? FormatSecureKeyStrengthMessage(v.Strength, v.Error, v.Recommendations) : string.Empty)
        .ToPropertyEx(this, x => x.SecureKeyStrengthMessage);
    
    this.WhenAnyValue(x => x.CurrentSecureKeyLength)
        .Select(_ => _hasSecureKeyBeenTouched)
        .ToPropertyEx(this, x => x.HasSecureKeyBeenTouched);

    IObservable<string> secureKeyErrorStream = secureKeyValidation
        .Select(v => _hasSecureKeyBeenTouched ? FormatSecureKeyStrengthMessage(v.Strength, v.Error, v.Recommendations) : string.Empty)
        .Replay(1)
        .RefCount();
    
    secureKeyErrorStream.ToPropertyEx(this, x => x.SecureKeyError);
    this.WhenAnyValue(x => x.SecureKeyError).Select(e => !string.IsNullOrEmpty(e))
        .ToPropertyEx(this, x => x.HasSecureKeyError);
    
    IObservable<bool> isSecureKeyLogicallyValid = secureKeyValidation.Select(v => string.IsNullOrEmpty(v.Error));
    
    IObservable<System.Reactive.Unit> verifyLengthTrigger = this
        .WhenAnyValue(x => x.CurrentVerifySecureKeyLength)
        .Select(_ => System.Reactive.Unit.Default);
    
    IObservable<System.Reactive.Unit> verifyValidationTrigger = verifyLengthTrigger
        .Merge(languageTrigger)
        .Merge(lengthTrigger);
    
    IObservable<bool> secureKeysMatch = verifyValidationTrigger
        .Select(_ => DoSecureKeysMatch())
        .Replay(1)
        .RefCount();
    
    IObservable<string> verifySecureKeyErrorStream = secureKeysMatch
        .Select(match => _hasVerifySecureKeyBeenTouched && !match
            ? LocalizationService["ValidationErrors.VerifySecureKey.DoesNotMatch"]
            : string.Empty)
        .Replay(1)
        .RefCount();

    verifySecureKeyErrorStream.ToPropertyEx(this, x => x.VerifySecureKeyError);
    this.WhenAnyValue(x => x.VerifySecureKeyError).Select(e => !string.IsNullOrEmpty(e))
        .ToPropertyEx(this, x => x.HasVerifySecureKeyError);

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
            (error, var recs) = SecureKeyValidator.Validate(secureKey, LocalizationService);
            strength = SecureKeyValidator.EstimatePasswordStrength(secureKey, LocalizationService);
            if (recs.Any())
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

    private static string FormatError(string? error, string recommendations)
    {
        if (!string.IsNullOrEmpty(error)) return error;
        return recommendations;
    }

    private string FormatSecureKeyStrengthMessage(PasswordStrength strength, string? error, string recommendations)
    {
        string strengthText = strength switch
        {
            PasswordStrength.Invalid     => LocalizationService["ValidationErrors.PasswordStrength.Invalid"],
            PasswordStrength.VeryWeak    => LocalizationService["ValidationErrors.PasswordStrength.VeryWeak"],
            PasswordStrength.Weak        => LocalizationService["ValidationErrors.PasswordStrength.Weak"],
            PasswordStrength.Good        => LocalizationService["ValidationErrors.PasswordStrength.Good"],
            PasswordStrength.Strong      => LocalizationService["ValidationErrors.PasswordStrength.Strong"],
            PasswordStrength.VeryStrong  => LocalizationService["ValidationErrors.PasswordStrength.VeryStrong"],
            _                            => LocalizationService["ValidationErrors.PasswordStrength.Invalid"]
        };

        string message = !string.IsNullOrEmpty(error) ? error : recommendations;
        return string.IsNullOrEmpty(message) ? strengthText : $"{strengthText}: {message}";
    }
    private async Task SubmitRegistrationSecureKeyAsync()
    {
        if (IsBusy || !CanSubmit) return;

        byte[]? secureKeyBytes = null;
        try
        {
            _secureKeyBuffer.WithSecureBytes(bytes =>
            {
                secureKeyBytes = new byte[bytes.Length];
                bytes.CopyTo(secureKeyBytes);
            });

            if (secureKeyBytes == null)
            {
                SecureKeyError = "Failed to process secure key";
                return;
            }

            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> opfrResult =
                OpaqueProtocolService.CreateOprfRequest(secureKeyBytes);

            if (opfrResult.IsErr)
            {
                SecureKeyError = opfrResult.UnwrapErr().Message;
                return;
            }

            (byte[] OprfRequest, BigInteger Blind) opfr = opfrResult.Unwrap();

            OpaqueRegistrationInitRequest request = new()
            {
                MembershipIdentifier = VerificationSessionId,
                PeerOprf = ByteString.CopyFrom(opfr.OprfRequest)
            };

            Result<Unit, NetworkFailure> createMembershipResult = await NetworkProvider.ExecuteUnaryRequestAsync(
                ComputeConnectId(),
                RpcServiceType.OpaqueRegistrationInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
                async payload =>
                {
                    OpaqueRegistrationInitResponse createMembershipResponse =
                        OpaqueRegistrationInitResponse.Parser.ParseFrom(payload);


                    if (createMembershipResponse.Result != OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
                    {
                        SecureKeyError = $"Registration failed: {createMembershipResponse.Message}";
                        return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    }

                    OpaqueProtocolService opaqueService = CreateOpaqueService();
                    Result<byte[], OpaqueFailure> registrationRecordResult =
                        opaqueService.CreateRegistrationRecord(
                            secureKeyBytes,
                            SecureByteStringInterop.WithByteStringAsSpan(createMembershipResponse.PeerOprf,
                                span => span.ToArray()),
                            opfr.Blind);

                    if (registrationRecordResult.IsErr)
                    {
                        SecureKeyError = registrationRecordResult.UnwrapErr().Message;
                        return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    }

                    byte[] registrationRecord = registrationRecordResult.Unwrap();

                    OpaqueRegistrationCompleteRequest completeRequest = new()
                    {
                        MembershipIdentifier = VerificationSessionId,
                        PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord)!
                    };

                    await NetworkProvider.ExecuteUnaryRequestAsync(
                        ComputeConnectId(),
                        RpcServiceType.OpaqueRegistrationComplete,
                        SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(),
                            span => span.ToArray()),
                        async completePayload =>
                        {
                            try
                            {
                                OpaqueRegistrationCompleteResponse completeResponse =
                                    OpaqueRegistrationCompleteResponse.Parser.ParseFrom(completePayload);


                                return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                            }
                            catch (Exception ex)
                            {
                                SecureKeyError = $"Error processing registration completion: {ex.Message}";
                                return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                            }
                        });

                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, true,
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            SecureKeyError = $"Submission failed: {ex.Message}";
        }
        finally
        {
            if (secureKeyBytes != null)
            {
                Array.Clear(secureKeyBytes, 0, secureKeyBytes.Length);
            }
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

    private OpaqueProtocolService CreateOpaqueService()
    {
        byte[] serverPublicKeyBytes = SecureByteStringInterop.WithByteStringAsSpan(
            NetworkProvider.ApplicationInstanceSettings.ServerPublicKey,
            span => span.ToArray());
        ECPublicKeyParameters serverStaticPublicKeyParam = new(
            OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(serverPublicKeyBytes),
            OpaqueCryptoUtilities.DomainParams
        );
        return new OpaqueProtocolService(serverStaticPublicKeyParam);
    }

    public void ResetState()
    {
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);
        _verifySecureKeyBuffer.Remove(0, _verifySecureKeyBuffer.Length);
        _hasSecureKeyBeenTouched = false;
        _hasVerifySecureKeyBeenTouched = false;
    }

    public string? UrlPathSegment { get; } = "/secure-key-confirmation";
    public IScreen HostScreen { get; }
}