using System.Globalization;
using System.Reactive;
using Ecliptix.Core.AppEvents.LanguageDetectionEvents;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class DetectLanguageDialogViewModel : ReactiveObject
{
    private readonly ILanguageDetectionEvents _languageDetectionEvents;

    public string Title { get; }
    public string PromptText { get; }
    public string ConfirmButtonText { get; }
    public string DeclineButtonText { get; }
    public string FlagPath { get; }
    private readonly string _targetCulture;

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    private static readonly LanguageConfiguration LanguageConfig = LanguageConfiguration.Default;

    public DetectLanguageDialogViewModel(
        ILocalizationService localizationService,
        ILanguageDetectionEvents languageDetectionEvents,
        NetworkProvider networkProvider
        )
    {
        _languageDetectionEvents = languageDetectionEvents;
        string country = networkProvider.ApplicationInstanceSettings.Country;
        _targetCulture = LanguageConfig.GetCultureByCountry(country);
        CultureInfo targetInfo = CultureInfo.GetCultureInfo(_targetCulture);

        Title = localizationService["LanguageDetection.Title"];
        PromptText = localizationService.GetString("LanguageDetection.Prompt", LanguageConfig.GetDisplayName(_targetCulture));
        ConfirmButtonText = localizationService["LanguageDetection.Button.Confirm"];
        DeclineButtonText = localizationService["LanguageDetection.Button.Decline"];

        LanguageItem? languageItem = LanguageConfig.GetLanguageByCode(_targetCulture);
        FlagPath = languageItem?.FlagImagePath ?? "avares://Ecliptix.Core/Assets/Icons/Flags/usa_flag.svg";

        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        DeclineCommand = ReactiveCommand.Create(OnDecline);
    }

    private void OnConfirm()
    {
        _languageDetectionEvents.Invoke(LanguageDetectionDialogEvent.Confirm(_targetCulture));
    }

    private void OnDecline()
    {
        _languageDetectionEvents.Invoke(LanguageDetectionDialogEvent.Decline());
    }
}
