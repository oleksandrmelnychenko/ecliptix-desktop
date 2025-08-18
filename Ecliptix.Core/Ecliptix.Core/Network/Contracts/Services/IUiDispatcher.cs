using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IUiDispatcher
{
    void Post(Action action);

    Task PostAsync(Func<Task> action);

    bool IsAvailable { get; }
}