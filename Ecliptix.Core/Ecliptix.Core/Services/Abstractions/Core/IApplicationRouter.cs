using System.Threading.Tasks;
using Avalonia.Controls;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IApplicationRouter
{
    Task NavigateToAuthenticationAsync();

    Task NavigateToMainAsync();

    Task TransitionFromSplashAsync(Window splashWindow, bool isAuthenticated);
}
