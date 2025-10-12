using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Abstractions.Core;

public enum ApplicationState
{
    Initializing,
    Anonymous,
    Authenticated
}

public interface IApplicationStateManager
{
    ApplicationState CurrentState { get; }

    IObservable<ApplicationState> StateChanges { get; }

    string? CurrentMembershipId { get; }

    Task TransitionToAnonymousAsync();

    Task TransitionToAuthenticatedAsync(string membershipId);
}
