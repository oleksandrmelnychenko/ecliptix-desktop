using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Core;

public enum ApplicationState
{
    INITIALIZING,
    Anonymous,
    Authenticated
}

public interface IApplicationStateManager
{
    ApplicationState CurrentState { get; }

    IObservable<ApplicationState> StateChanges { get; }

    Option<string> CurrentMembershipId { get; }

    Task TransitionToAnonymousAsync();

    Task TransitionToAuthenticatedAsync(string membershipId);
}
