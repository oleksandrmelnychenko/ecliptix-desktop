using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Modals;

public sealed class RedirectNotificationViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    public ReactiveCommand<Unit, Unit> SkipDelayCommand { get; }

    private readonly ILocalizationService _localizationService;
    private bool _disposed;

    [Reactive] public string Title { get; set; }
    [Reactive] public string Subtitle { get; set; }
    [Reactive] public string Message { get; set; }

    [Reactive] public int SecondsRemaining { get; set; }

    [Reactive] public string AutoRequestPrefixText { get; set; } = string.Empty;

    public RedirectNotificationViewModel(
        string title,
        string subtitle,
        string message,
        int totalSeconds,
        Action onComplete,
        ILocalizationService localizationService)
    {
        _localizationService = localizationService;

        Title = title;
        Subtitle = subtitle;
        Message = message;
        SecondsRemaining = totalSeconds;

        SkipDelayCommand = ReactiveCommand.Create(onComplete);

        UpdateLocalizedStrings();

        this.WhenActivated(disposables =>
        {
            int ticks = 0;
            bool hasCompleted = false;

            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateLocalizedStrings())
                .DisposeWith(disposables);

            Observable.Interval(TimeSpan.FromSeconds(1))
                .StartWith(0)
                .ObserveOn(RxApp.MainThreadScheduler)
                .TakeUntil(SkipDelayCommand)
                .TakeWhile(_ => !hasCompleted)
                .Subscribe(_ =>
                {
                    int newRemaining = Math.Max(0, totalSeconds - ticks);

                    if (newRemaining != SecondsRemaining)
                    {
                        SecondsRemaining = newRemaining;
                    }

                    ticks++;

                    if (SecondsRemaining <= 0 && !hasCompleted)
                    {
                        hasCompleted = true;
                        onComplete();
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private void UpdateLocalizedStrings() => AutoRequestPrefixText = _localizationService[LocalizationKeys.Verification.Redirect.AUTO_REQUEST_PREFIX];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
