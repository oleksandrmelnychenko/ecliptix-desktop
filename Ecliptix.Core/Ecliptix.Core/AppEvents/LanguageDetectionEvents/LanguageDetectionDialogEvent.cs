namespace Ecliptix.Core.AppEvents.LanguageDetectionEvents;

public sealed record LanguageDetectionDialogEvent
{
    public LanguageDetectionAction Action { get; init; }
    public string? TargetCulture { get; init; }

    private LanguageDetectionDialogEvent(LanguageDetectionAction action, string? targetCulture)
    {
        Action = action;
        TargetCulture = targetCulture;
    }

    public static LanguageDetectionDialogEvent Decline() =>
        new(LanguageDetectionAction.Decline, null);

    public static LanguageDetectionDialogEvent Confirm(string targetCulture) =>
        new(LanguageDetectionAction.Confirm, targetCulture);
}