using System;

namespace Ecliptix.Core.AppEvents.LanguageDetectionEvents;

public interface ILanguageDetectionEvents
{
    IObservable<LanguageDetectionDialogEvent> LanguageDetectionRequested { get; }
    void Invoke(LanguageDetectionDialogEvent languageDetectionEvent);
}