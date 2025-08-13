using System.Reactive;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.LanguageDetectionEvents;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class DetectLanguageDialogVideModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }
    private readonly ILanguageDetectionEvents _languageDetectionEvents;
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    public DetectLanguageDialogVideModel(
        ILocalizationService localizationService,
        ILanguageDetectionEvents languageDetectionEvents)
    {
        _languageDetectionEvents = languageDetectionEvents;
        LocalizationService = localizationService;
        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        DeclineCommand = ReactiveCommand.Create(OnDecline);
    }

    private void OnConfirm()
    {
        _languageDetectionEvents.Invoke(LanguageDetectionDialogEvent.Confirm("uk-UA"));
    }

    private void OnDecline()
    {
        _languageDetectionEvents.Invoke(LanguageDetectionDialogEvent.Decline());
    }
}
