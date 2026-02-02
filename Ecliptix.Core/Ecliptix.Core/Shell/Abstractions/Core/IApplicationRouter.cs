using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Shell.Services.Core;

namespace Ecliptix.Core.Shell.Abstractions.Core;

public interface IApplicationRouter
{
    Task NavigateToAuthenticationAsync();

    Task NavigateToMainAsync();

    Task TransitionFromSplashAsync(
        Window splashWindow,
        StartupLaunchMode launchMode,
        Ecliptix.Protobuf.Membership.Membership.Types.CreationStatus creationStatus);


}
