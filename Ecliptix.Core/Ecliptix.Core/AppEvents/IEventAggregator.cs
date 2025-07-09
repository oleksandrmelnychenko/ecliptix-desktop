using System;

namespace Ecliptix.Core.AppEvents;

public interface IEventAggregator
{
    IObservable<TMessage> GetEvent<TMessage>();

    void Publish<TMessage>(TMessage message);
}
