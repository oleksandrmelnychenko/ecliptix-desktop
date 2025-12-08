using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Abstractions.Core;

public enum ApplicationState
{
    INITIALIZING,
    ANONYMOUS,
    AUTHENTICATED
}

public interface IApplicationStateManager
{
    ApplicationState CurrentState { get; }

    Task TransitionToAnonymousAsync();

    Task TransitionToAuthenticatedAsync(string membershipId);
}
