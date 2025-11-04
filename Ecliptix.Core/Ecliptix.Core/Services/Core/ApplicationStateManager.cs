using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Core;

public sealed class ApplicationStateManager : IApplicationStateManager, IDisposable
{
    private readonly BehaviorSubject<ApplicationState> _stateSubject = new(ApplicationState.INITIALIZING);
    private Option<string> _currentMembershipId = Option<string>.None;
    private bool _disposed;

    public ApplicationState CurrentState => _stateSubject.Value;

    public IObservable<ApplicationState> StateChanges => _stateSubject;

    public Option<string> CurrentMembershipId => _currentMembershipId;

    public Task TransitionToAnonymousAsync()
    {
        _currentMembershipId = Option<string>.None;
        _stateSubject.OnNext(ApplicationState.Anonymous);
        return Task.CompletedTask;
    }

    public Task TransitionToAuthenticatedAsync(string membershipId)
    {
        if (string.IsNullOrEmpty(membershipId))
        {
            throw new ArgumentException("Membership ID cannot be null or empty", nameof(membershipId));
        }

        _currentMembershipId = Option<string>.Some(membershipId);
        _stateSubject.OnNext(ApplicationState.Authenticated);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stateSubject?.Dispose();
        _disposed = true;
    }
}
