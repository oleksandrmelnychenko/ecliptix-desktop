using System;

namespace Ecliptix.Core.AppEvents.System;

public interface ISystemEvents
{
    IObservable<SystemStateChangedEvent> SystemStateChanged { get; }
    void Publish(SystemStateChangedEvent message);
}