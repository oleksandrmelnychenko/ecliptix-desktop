using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;

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
