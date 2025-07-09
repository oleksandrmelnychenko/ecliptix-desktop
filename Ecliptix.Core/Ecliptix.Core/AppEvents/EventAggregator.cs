using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Ecliptix.Core.AppEvents;

public sealed class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, object> _subjects = new();

    public IObservable<TMessage> GetEvent<TMessage>()
    {
        ISubject<TMessage> subject = (ISubject<TMessage>)_subjects.GetOrAdd(
            typeof(TMessage),
            _ => new Subject<TMessage>());

        return subject.AsObservable();
    }

    public void Publish<TMessage>(TMessage message)
    {
        if (_subjects.TryGetValue(typeof(TMessage), out var subjectObject))
        {
            ISubject<TMessage> subject = (ISubject<TMessage>)subjectObject;
            subject.OnNext(message);
        }
    }
}