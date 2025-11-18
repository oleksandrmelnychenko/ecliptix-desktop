using System;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum ProfileMenuAnimationType
{
    SHOW,
    HIDE
}

public sealed record ProfileMenuCommandEvent
{
    public ProfileMenuAnimationType AnimationType { get; }
    public DateTime Timestamp { get; }

    private ProfileMenuCommandEvent(ProfileMenuAnimationType animationType)
    {
        AnimationType = animationType;
        Timestamp = DateTime.UtcNow;
    }

    public static ProfileMenuCommandEvent Show() => new(ProfileMenuAnimationType.SHOW);
    public static ProfileMenuCommandEvent Hide() => new(ProfileMenuAnimationType.HIDE);
    public static ProfileMenuCommandEvent Toggle(bool isCurrentlyOpen) =>
        isCurrentlyOpen ? Hide() : Show();
}

public sealed record ProfileMenuAnimationCompleteEvent
{
    public ProfileMenuAnimationType AnimationType { get; }
    public DateTime Timestamp { get; }

    private ProfileMenuAnimationCompleteEvent(ProfileMenuAnimationType animationType)
    {
        AnimationType = animationType;
        Timestamp = DateTime.UtcNow;
    }

    public static ProfileMenuAnimationCompleteEvent ShowComplete() => new(ProfileMenuAnimationType.SHOW);
    public static ProfileMenuAnimationCompleteEvent HideComplete() => new(ProfileMenuAnimationType.HIDE);
}
