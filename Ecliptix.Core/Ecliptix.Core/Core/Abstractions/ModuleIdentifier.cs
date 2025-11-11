using System;

namespace Ecliptix.Core.Core.Abstractions;

public enum ModuleIdentifier
{
    AUTHENTICATION,
    MAIN,
    FEED,
    CHATS,
    SETTINGS,
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
        _ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "Unknown module identifier")
    };
}
