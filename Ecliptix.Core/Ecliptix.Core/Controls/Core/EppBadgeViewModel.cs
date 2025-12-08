using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Core;

public class EppBadgeViewModel: ReactiveObject
{
    [Reactive] public string Text { get; set; }
    [Reactive] public string TooltipTitle { get; set; }
    [Reactive] public string TooltipFeature1 { get; set; }
    [Reactive] public string TooltipFeature2 { get; set; }
    [Reactive] public string TooltipFeature3 { get; set; }
    public string Icon { get; } = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

    public EppBadgeViewModel(ILocalizationService localizationService)
    {
        Text = "Protected by EPP";
        TooltipTitle = localizationService[LocalizationKeys.EcliptixProtectionProtocol.TITLE];
        TooltipFeature1 = localizationService[LocalizationKeys.EcliptixProtectionProtocol.FEATURE_END_TO_END_ENCRYPTION];
        TooltipFeature2 = localizationService[LocalizationKeys.EcliptixProtectionProtocol.FEATURE_FORWARD_SECRECY];
        TooltipFeature3 = localizationService[LocalizationKeys.EcliptixProtectionProtocol.FEATURE_OPAQUE_PROTOCOL];
    }
}
