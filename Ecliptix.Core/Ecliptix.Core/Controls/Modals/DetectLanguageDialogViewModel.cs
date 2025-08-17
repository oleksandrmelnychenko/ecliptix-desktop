using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.LanguageDetectionEvents;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Configuration;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels;
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

    private static readonly FrozenDictionary<string, string> SupportedCountries = LanguageConfiguration.SupportedCountries;

    private static readonly FrozenDictionary<string, string> FlagMap = LanguageConfiguration.FlagMap;

    public DetectLanguageDialogViewModel(
        ILocalizationService localizationService,
        ILanguageDetectionEvents languageDetectionEvents,
        NetworkProvider networkProvider
        )
    {
        _languageDetectionEvents = languageDetectionEvents;
        string country = networkProvider.ApplicationInstanceSettings.Country;
        _targetCulture = SupportedCountries.GetValueOrDefault(country, "en-US");
        CultureInfo targetInfo = CultureInfo.GetCultureInfo(_targetCulture);

        Title = localizationService["LanguageDetection.Title"];
        PromptText = localizationService.GetString("LanguageDetection.Prompt", (targetInfo.EnglishName).Split('(')[0].Trim());
        ConfirmButtonText = localizationService["LanguageDetection.Button.Confirm"];
        DeclineButtonText = localizationService["LanguageDetection.Button.Decline"];
        FlagPath = FlagMap.GetValueOrDefault(_targetCulture, "avares://Ecliptix.Core/Assets/Flags/usa_flag.svg");

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
