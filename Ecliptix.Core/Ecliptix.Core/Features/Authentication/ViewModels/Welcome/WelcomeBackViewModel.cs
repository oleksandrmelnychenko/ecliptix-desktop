using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeBackViewModel: ViewModelBase, IRoutableViewModel, IResettable
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
                settings.Membership.UniqueIdentifier != null &&
                !settings.Membership.UniqueIdentifier.IsEmpty)
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
            string errorMessage = LocalizationService["Session data missing. Please sign in again."];

            await StartAutoRedirectSequenceAsync(
                HostScreen,
                errorMessage,
                3,
                (host) =>
                {
                    host.ClearNavigationStack(preserveInitialWelcome: true);
                    host.NavigateBack.Execute().Subscribe();
                });
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
