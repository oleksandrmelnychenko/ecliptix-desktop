using System;

namespace Ecliptix.Core.Core.Messaging.Events;

/// <summary>
/// Language detection action types
/// </summary>
public enum LanguageDetectionAction
{
    Decline,
    Confirm
}

/// <summary>
/// Event fired for language detection dialog actions
/// Compatible with existing LanguageDetectionDialogEvent from AppEvents
/// </summary>
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
        new(LanguageDetectionAction.Decline, null);

    public static LanguageDetectionDialogEvent Confirm(string targetCulture) =>
        new(LanguageDetectionAction.Confirm, targetCulture);
}