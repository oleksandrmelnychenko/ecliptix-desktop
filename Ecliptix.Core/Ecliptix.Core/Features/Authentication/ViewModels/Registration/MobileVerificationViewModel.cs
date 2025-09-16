using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protobuf.Protocol;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IUiDispatcher _uiDispatcher;

    [Reactive] public string? NetworkErrorMessage { get; private set; } = string.Empty;

    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    [ObservableAsProperty] public bool IsBusy { get; }

    [Reactive] public string? MobileNumberError { get; set; }
    [Reactive] public bool HasMobileNumberError { get; set; }

    public ReactiveCommand<Unit, Unit>? VerifyMobileNumberCommand { get; private set; }

    public MobileVerificationViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IUiDispatcher uiDispatcher) : base(systemEventService, networkProvider, localizationService)
    {
        _registrationService = registrationService;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _uiDispatcher = uiDispatcher;
        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<Unit> languageTrigger = LanguageChanged;

        IObservable<Unit> mobileTrigger = this
            .WhenAnyValue(x => x.MobileNumber)
            .Select(_ => Unit.Default);

        IObservable<Unit> validationTrigger =
            mobileTrigger
                .Merge(languageTrigger);

        IObservable<string> mobileValidation = validationTrigger
            .Select(_ => MobileNumberValidator.Validate(MobileNumber, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.MobileNumber, x => x.NetworkErrorMessage)
            .CombineLatest(mobileValidation, (inputs, validationError) =>
            {
                string mobile = inputs.Item1;
                string? networkError = inputs.Item2;

                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                    _hasMobileNumberBeenTouched = true;

                if (!string.IsNullOrEmpty(networkError))
                    return networkError;

                return !_hasMobileNumberBeenTouched ? string.Empty : validationError;
            })
            .Replay(1)
            .RefCount();

        mobileErrorStream
            .Subscribe(error =>
            {
                MobileNumberError = error;
                HasMobileNumberError = !string.IsNullOrEmpty(error);
            })
            .DisposeWith(_disposables);

        return mobileValidation
            .Select(string.IsNullOrEmpty)
            .DistinctUntilChanged();
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canVerify = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canVerify);
        VerifyMobileNumberCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        _disposables.Add(VerifyMobileNumberCommand);
    }

    private async Task<Unit> ExecuteVerificationAsync()
    {
        if (_isDisposed) return Unit.Default;

        NetworkErrorMessage = string.Empty;

        try
        {
            using CancellationTokenSource timeoutCts = new(AuthenticationConstants.Timeouts.PhoneValidationTimeout);

            string systemDeviceIdentifier = SystemDeviceIdentifier();
            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            Task<Result<ByteString, string>> validationTask = _registrationService.ValidatePhoneNumberAsync(
                MobileNumber,
                systemDeviceIdentifier,
                connectId);

            Result<ByteString, string> result = await validationTask.WaitAsync(timeoutCts.Token);

            if (_isDisposed) return Unit.Default;

            if (result.IsOk)
            {
                ByteString mobileNumberIdentifier = result.Unwrap();

                VerifyOtpViewModel vm = new(SystemEventService, NetworkProvider, LocalizationService, HostScreen,
                    mobileNumberIdentifier, _applicationSecureStorageProvider, _registrationService, _uiDispatcher);

                if (!_isDisposed && HostScreen is MembershipHostWindowModel hostWindow)
                {
                    hostWindow.NavigateToViewModel(vm);
                }
            }
            else if (!_isDisposed)
            {
                NetworkErrorMessage = result.UnwrapErr();
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (TimeoutException)
        {
            if (!_isDisposed)
            {
                Log.Warning("Phone validation timed out after {Timeout}ms",
                    AuthenticationConstants.Timeouts.PhoneValidationTimeout.TotalMilliseconds);
                NetworkErrorMessage = LocalizationService["Errors.ValidationTimeout"];
            }
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                Log.Error(ex, "Mobile verification failed");
                NetworkErrorMessage = LocalizationService["Errors.NetworkError"];
            }
        }

        return Unit.Default;
    }

    public async void HandleEnterKeyPress()
    {
        if (_isDisposed) return;

        try
        {
            if (VerifyMobileNumberCommand != null && await VerifyMobileNumberCommand.CanExecute.FirstOrDefaultAsync())
            {
                VerifyMobileNumberCommand.Execute().Subscribe().DisposeWith(_disposables);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("HandleEnterKeyPress failed: {Error}", ex.Message);
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        MobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        NetworkErrorMessage = string.Empty;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}