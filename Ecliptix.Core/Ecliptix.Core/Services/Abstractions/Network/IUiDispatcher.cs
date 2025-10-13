using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IUiDispatcher
{
    void Post(Action action);

    Task PostAsync(Func<Task> action);

    bool IsAvailable { get; }
}
