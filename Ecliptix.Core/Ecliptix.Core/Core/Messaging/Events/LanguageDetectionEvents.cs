using System;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum LanguageDetectionAction
{
    DECLINE,
    CONFIRM,
    REQUEST
}

public sealed record LanguageDetectionDialogEvent
{
    public LanguageDetectionAction Action { get; }
    public string? TargetCulture { get; }
    public DateTime Timestamp { get; }

    private LanguageDetectionDialogEvent(LanguageDetectionAction action, string? targetCulture)
    {
        Action = action;
        TargetCulture = targetCulture;
        Timestamp = DateTime.UtcNow;
    }

    public static LanguageDetectionDialogEvent Decline() =>
        new(LanguageDetectionAction.DECLINE, null);

    public static LanguageDetectionDialogEvent Confirm(string targetCulture) =>
        new(LanguageDetectionAction.CONFIRM, targetCulture);

    public static LanguageDetectionDialogEvent Request(string targetCulture) =>
        new(LanguageDetectionAction.REQUEST, targetCulture);
}
