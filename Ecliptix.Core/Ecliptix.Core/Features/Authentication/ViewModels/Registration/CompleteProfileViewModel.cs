using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Account;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class CompleteProfileViewModel : ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly CompositeDisposable _disposables = new();

    private const int CURRENT_STEP = 4;

    private bool _isDisposed;

    private readonly Subject<string> _executionErrorSubject = new();
    public IObservable<string> ExecutionError => _executionErrorSubject.AsObservable();

    public CompleteProfileViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService, connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        IObservable<bool> isFormValid = SetupValidation();

        SetupCommands(isFormValid);
    }

    public string StepBadgeText => string.Format(
        LocalizationService[LocalizationKeys.Verification.Info.STEP_OF],
        CURRENT_STEP,
        TOTAL_STEPS);

    public string? UrlPathSegment { get; } = "/complete-profile";
    public IScreen HostScreen { get; }

    [Reactive] public string ProfileName { get; set; } = string.Empty;
    [Reactive] public string DisplayName { get; set; } = string.Empty;

    [Reactive] public string ProfileNameError { get; private set; } = string.Empty;
    [Reactive] public bool HasProfileNameError { get; private set; }

    [Reactive] public string DisplayNameError { get; private set; } = string.Empty;
    [Reactive] public bool HasDisplayNameError { get; private set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CompleteSetupCommand { get; private set; } = null!;

    public async Task HandleEnterKeyPressAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (await CompleteSetupCommand.CanExecute.FirstOrDefaultAsync())
        {
            CompleteSetupCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    public void ResetState()
    {
        ProfileName = string.Empty;
        DisplayName = string.Empty;

        ProfileNameError = string.Empty;
        HasProfileNameError = false;

        DisplayNameError = string.Empty;
        HasDisplayNameError = false;

        _executionErrorSubject.OnNext(string.Empty);
    }

    private IObservable<bool> SetupValidation()
    {
        this.WhenAnyValue(x => x.ProfileName)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                bool isValid = !string.IsNullOrWhiteSpace(name) && name.Length >= 3;
                ProfileNameError = isValid ? string.Empty : LocalizationService[LocalizationKeys.ValidationErrors.Profile.INVALID_NAME];
                HasProfileNameError = !isValid;
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.DisplayName)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                bool isValid = !string.IsNullOrWhiteSpace(name);
                DisplayNameError = isValid ? string.Empty : LocalizationService[LocalizationKeys.ValidationErrors.Profile.INVALID_DISPLAY_NAME];
                HasDisplayNameError = !isValid;
            })
            .DisposeWith(_disposables);

        return this.WhenAnyValue(
            x => x.HasProfileNameError,
            x => x.HasDisplayNameError,
            x => x.ProfileName,
            x => x.DisplayName,
            (nameErr, dispErr, name, disp) =>
                !nameErr && !dispErr &&
                !string.IsNullOrEmpty(name) &&
                !string.IsNullOrEmpty(disp)
        );
    }

    private void SetupCommands(IObservable<bool> isFormValid)
    {
        IObservable<bool> canExecute = isFormValid.CombineLatest(
            this.WhenAnyValue(x => x.IsBusy),
            (valid, busy) => valid && !busy
        );

        CompleteSetupCommand = ReactiveCommand.CreateFromTask(ExecuteCompletionAsync, canExecute);

        CompleteSetupCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        CompleteSetupCommand.ThrownExceptions
             .Subscribe(ex => _executionErrorSubject.OnNext(ex.Message))
             .DisposeWith(_disposables);
    }

   private async Task ExecuteCompletionAsync(CancellationToken cancellationToken)
    {
        try
        {
            Option<Guid> accountIdOpt = await GetCurrentAccountIdAsync();

            if (!accountIdOpt.IsSome)
            {
                _executionErrorSubject.OnNext(LocalizationService["Common.Error.SessionExpired"]);
                return;
            }

            Guid currentAccountId = accountIdOpt.Value;

            CreateOrUpdateProfileRequest request = new()
            {
                AccountId = Helpers.GuidToByteString(currentAccountId),
                ProfileName = ProfileName,
                DisplayName = DisplayName
            };

            TaskCompletionSource<CreateOrUpdateProfileResponse> responseSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            Result<EUnit, NetworkFailure> networkResult = await NetworkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.CreateOrUpdateProfile,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    CreateOrUpdateProfileResponse response = Helpers.ParseFromBytes<CreateOrUpdateProfileResponse>(payload);
                    responseSource.TrySetResult(response);
                    return Task.FromResult(Result<EUnit, NetworkFailure>.Ok(EUnit.Value));
                },
                allowDuplicates: false,
                token: cancellationToken
            );

            if (networkResult.IsErr)
            {
                Log.Error("[COMPLETE-PROFILE-VM] Network error: {Error}", networkResult.UnwrapErr().Message);
                _executionErrorSubject.OnNext(networkResult.UnwrapErr().Message);
                return;
            }

            CreateOrUpdateProfileResponse response = await responseSource.Task;

            if (response.IsSuccess)
            {
                Log.Information("Profile created successfully for AccountId: {AccountId}", currentAccountId);


                if (HostScreen is AuthenticationViewModel authHost)
                {
                    await authHost.SwitchToMainWindowCommand.Execute();
                }
            }
            else
            {
                _executionErrorSubject.OnNext(LocalizationService["Verification.Error.ProfileCreationFailed"]);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[COMPLETE-PROFILE-VM] Unexpected error completing profile");
            _executionErrorSubject.OnNext(LocalizationService["Common.Error.Unexpected"]);
        }
    }

    private async Task<Option<Guid>> GetCurrentAccountIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (settingsResult.IsOk)
        {
            ApplicationInstanceSettings settings = settingsResult.Unwrap();

            if (settings.CurrentAccountId != null && !settings.CurrentAccountId.IsEmpty)
            {
                return Option<Guid>.Some(Helpers.FromByteStringToGuid(settings.CurrentAccountId));
            }
        }

        Log.Warning("[COMPLETE-PROFILE-VM] Cannot retrieve AccountId from storage.");
        return Option<Guid>.None;
    }

    public new void Dispose() => Dispose(true);

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
            _executionErrorSubject.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
