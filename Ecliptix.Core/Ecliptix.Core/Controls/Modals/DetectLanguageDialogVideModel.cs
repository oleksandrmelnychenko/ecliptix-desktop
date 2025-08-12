using System.Reactive;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public class DetectLanguageDialogVideModel : ReactiveObject
{
    public ILocalizationService LocalizationService { get; }
    
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    public DetectLanguageDialogVideModel(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        DeclineCommand = ReactiveCommand.Create(OnDecline);
    }

    private void OnConfirm()
    {
        // TODO: Implement language confirmation logic
    }

    private void OnDecline()
    {
        // TODO: Implement language decline logic
    }
}
