using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public class EppBadgeViewModel: ReactiveObject
{
    public ILocalizationService LocalizationService { get; }
    public string Icon { get; } = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

    public EppBadgeViewModel(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
    }
}
