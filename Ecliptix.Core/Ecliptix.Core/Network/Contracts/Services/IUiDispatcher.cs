using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Network.Contracts.Services;

/// <summary>
/// Abstraction for dispatching actions to the UI thread.
/// This allows the retry strategy to be decoupled from specific UI frameworks.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Dispatches an action to the UI thread if available.
    /// If no UI thread is available (e.g., in headless scenarios), the action may be ignored or executed synchronously.
    /// </summary>
    /// <param name="action">The action to dispatch</param>
    void Post(Action action);

    /// <summary>
    /// Dispatches an async action to the UI thread if available.
    /// If no UI thread is available (e.g., in headless scenarios), the action may be ignored or executed directly.
    /// </summary>
    /// <param name="action">The async action to dispatch</param>
    /// <returns>A task representing the completion of the dispatched action</returns>
    Task PostAsync(Func<Task> action);

    /// <summary>
    /// Indicates whether the UI dispatcher is available and functional.
    /// Returns false in headless scenarios or when the UI framework is not available.
    /// </summary>
    bool IsAvailable { get; }
}