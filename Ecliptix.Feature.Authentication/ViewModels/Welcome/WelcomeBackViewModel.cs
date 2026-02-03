using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.MVVM;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Welcome;

public sealed class WelcomeBackViewModel : ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly IApplicationSecureStorageProvider _storageProvider;
    private readonly IGlobalModalService _globalModalService;
    private Membership.Types.CreationStatus _resumeStatus =
        Protobuf.Membership.Membership.Types.CreationStatus.Unspecified;
    private string _currentDescriptionKey = LocalizationKeys.Authentication.WelcomeBack.DESCRIPTION_OTP_VERIFIED;
    public string DescriptionText => LocalizationService[_currentDescriptionKey];

    public WelcomeBackViewModel(
        IScreen hostScreen,
        ILocalizationService localizationService,
        NetworkProvider networkProvider,
        IGlobalModalService globalModalService,
        IApplicationSecureStorageProvider storageProvider)
        : base(networkProvider, localizationService, globalModalService)
    {
        HostScreen = hostScreen;
        _storageProvider = storageProvider;
        _globalModalService = globalModalService;

        InitializeCommands();
    }

    public string UrlPathSegment => "/welcome-back";

    public IScreen HostScreen { get; }

    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<Unit, Unit> ContinueToSetupCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, IRoutableViewModel?> ContinueLaterCommand { get; private set; } = null!;
    public void ResetState()
    {
        Task.Run(async () =>
        {
            try
            {
                await _globalModalService.CloseAllAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WELCOME-BACK] Failed to close modals during reset");
            }
        });
    }

    public void SetupResumeState(Membership.Types.CreationStatus status)
    {
        _resumeStatus = status;

        _currentDescriptionKey = status switch
        {
            Protobuf.Membership.Membership.Types.CreationStatus.SecureKeySet =>
                LocalizationKeys.Authentication.WelcomeBack.DESCRIPTION_SECURE_KEY_SET,

            _ => LocalizationKeys.Authentication.WelcomeBack.DESCRIPTION_OTP_VERIFIED
        };

        Log.Information("[WELCOME-BACK] Resume state setup with status: {Status}. Key: {Key}", status, _currentDescriptionKey);

        this.RaisePropertyChanged(nameof(DescriptionText));
    }

    private void InitializeCommands()
    {
        ContinueToSetupCommand = ReactiveCommand.CreateFromTask(ExecuteContinueToSetupAsync);

        ContinueLaterCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            hostWindow.ClearNavigationStack(preserveInitialWelcome: true);
            return hostWindow.NavigateBack.Execute();
        });

        ContinueToSetupCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        _disposables.Add(ContinueToSetupCommand);
        _disposables.Add(ContinueLaterCommand);
    }

    private async Task ExecuteContinueToSetupAsync()
    {
        AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;

        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _storageProvider.GetApplicationInstanceSettingsAsync();

        bool hasValidMembership = false;
        if (settingsResult.IsOk)
        {
            ApplicationInstanceSettings settings = settingsResult.Unwrap();
            if (settings.Membership != null &&
                settings.Membership.MembershipId != null &&
                !settings.Membership.MembershipId.IsEmpty)
            {
                hasValidMembership = true;
            }
        }

        if (hasValidMembership)
        {
            hostWindow.CurrentFlowContext = AuthenticationFlowContext.REGISTRATION;

            MembershipViewType targetView = _resumeStatus switch
            {
                Protobuf.Membership.Membership.Types.CreationStatus.OtpVerified => MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW,

                Protobuf.Membership.Membership.Types.CreationStatus.SecureKeySet => MembershipViewType.COMPLETE_PROFILE_VIEW,

                _ => MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW
            };

            Log.Information("[WELCOME-BACK] Continuing registration to view: {TargetView}", targetView);
            hostWindow.Navigate.Execute(targetView).Subscribe();
        }
        else
        {
            string title = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_TITLE];
            string subtitle = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_SUBTITLE];
            string message = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_SESSION_MISSING];

            await StartAutoRedirectSequenceAsync(
                HostScreen,
                message,
                66,
                (host) =>
                {
                    host.ClearNavigationStack();
                    host.Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                },
                title,
                subtitle
            );
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables.Dispose();
        }
        base.Dispose(disposing);
    }
}
