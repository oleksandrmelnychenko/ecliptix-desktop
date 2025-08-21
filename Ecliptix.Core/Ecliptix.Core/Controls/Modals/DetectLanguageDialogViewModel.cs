using System.Globalization;
using System.Reactive;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class DetectLanguageDialogViewModel : ReactiveObject
{
    private readonly IUnifiedMessageBus _messageBus;

    public string Title { get; }
    public string PromptText { get; }
    public string ConfirmButtonText { get; }
    public string DeclineButtonText { get; }
    public string FlagPath { get; }
    private readonly string _targetCulture;

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    public DetectLanguageDialogViewModel(
        ILocalizationService localizationService,
        IUnifiedMessageBus messageBus,
        NetworkProvider networkProvider
        )
    {
        _messageBus = messageBus;
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

    private async void OnConfirm()
    {
        await _messageBus.PublishAsync(LanguageDetectionDialogEvent.Confirm(_targetCulture));
    }

    private async void OnDecline()
    {
        await _messageBus.PublishAsync(LanguageDetectionDialogEvent.Decline());
    }
}
