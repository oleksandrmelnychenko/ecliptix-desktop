namespace Ecliptix.Core.Modularity;

public enum ModuleIdentifier
{
    AUTHENTICATION,
    MAIN,
    FEED,
    CHATS,
    SETTINGS,
    PROFILE,
    NEW_CONTENT
}

public static class ModuleIdentifierExtensions
{
    public static string ToName(this ModuleIdentifier identifier) => identifier switch
    {
        ModuleIdentifier.AUTHENTICATION => "Authentication",
        ModuleIdentifier.MAIN => "Main",
        ModuleIdentifier.FEED => "Feed",
        ModuleIdentifier.CHATS => "Chats",
        ModuleIdentifier.SETTINGS => "Settings",
        ModuleIdentifier.PROFILE => "Profile",
        ModuleIdentifier.NEW_CONTENT => "NewContent",
        _ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "Unknown module identifier")
    };
}
