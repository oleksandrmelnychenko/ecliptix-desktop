using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.Core;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public class ApplicationStateManager : IApplicationStateManager, IDisposable
{
    private readonly BehaviorSubject<ApplicationState> _stateSubject;
    private string? _currentMembershipId;
    private bool _disposed;

    public ApplicationStateManager()
    {
        _stateSubject = new BehaviorSubject<ApplicationState>(ApplicationState.Initializing);
    }

    public ApplicationState CurrentState => _stateSubject.Value;

    public IObservable<ApplicationState> StateChanges => _stateSubject;

    public string? CurrentMembershipId => _currentMembershipId;

    public Task TransitionToAnonymousAsync()
    {
        Log.Information("[APPLICATION-STATE] Transitioning to Anonymous state");
        _currentMembershipId = null;
        _stateSubject.OnNext(ApplicationState.Anonymous);
        return Task.CompletedTask;
    }

    public Task TransitionToAuthenticatedAsync(string membershipId)
    {
        if (string.IsNullOrEmpty(membershipId))
            throw new ArgumentException("Membership ID cannot be null or empty", nameof(membershipId));

        Log.Information("[APPLICATION-STATE] Transitioning to Authenticated state. MembershipId: {MembershipId}",
            membershipId);
        _currentMembershipId = membershipId;
        _stateSubject.OnNext(ApplicationState.Authenticated);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _stateSubject?.Dispose();
        _disposed = true;
    }
}
