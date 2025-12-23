using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Modals;

public sealed partial class RedirectNotificationViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    public ReactiveCommand<Unit, Unit> SkipDelayCommand { get; }

    private readonly ILocalizationService _localizationService;
    private bool _disposed;

    [Reactive] public string Title { get; set; }
    [Reactive] public string Subtitle { get; set; }
    [Reactive] public string Message { get; set; }

    [Reactive] public int SecondsRemaining { get; set; }

    [Reactive] public string AutoRequestPrefixText { get; set; }

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

        SkipDelayCommand = ReactiveCommand.Create(() =>
        {
            onComplete();
        });

        UpdateLocalizedStrings();

        this.WhenActivated(disposables =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

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
                .Subscribe(_ =>
                {
                    double elapsed = stopwatch.Elapsed.TotalSeconds;
                    int newRemaining = Math.Max(0, (int)Math.Ceiling(totalSeconds - elapsed));

                    if (newRemaining != SecondsRemaining)
                    {
                        SecondsRemaining = newRemaining;
                    }

                    if (SecondsRemaining <= 0)
                    {
                        stopwatch.Stop();
                        onComplete();
                    }
                })
                .DisposeWith(disposables);

            Disposable.Create(() => stopwatch.Stop()).DisposeWith(disposables);
        });
    }

    private void UpdateLocalizedStrings()
    {
        AutoRequestPrefixText = _localizationService[LocalizationKeys.Verification.Redirect.AUTO_REQUEST_PREFIX];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
