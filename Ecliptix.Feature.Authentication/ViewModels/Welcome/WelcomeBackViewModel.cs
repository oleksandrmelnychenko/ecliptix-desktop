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
            hostWindow.Navigate.Execute(MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW).Subscribe();
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
                    host.ClearNavigationStack(preserveInitialWelcome: true);
                    host.NavigateBack.Execute().Subscribe();
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
