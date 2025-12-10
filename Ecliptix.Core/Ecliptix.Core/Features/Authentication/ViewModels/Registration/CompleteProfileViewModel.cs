using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class CompleteProfileViewModel : ViewModelBase, IRoutableViewModel, IResettable
{
    public CompleteProfileViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IConnectivityService? connectivityService = null)
        : base(networkProvider,
            localizationService,
            connectivityService)
    {
        HostScreen = hostScreen;

    }

    public string? UrlPathSegment { get; } = "/complete-profile";

    public IScreen HostScreen { get; }

    public void ResetState() => throw new System.NotImplementedException();
}
