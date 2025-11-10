using System;

namespace Ecliptix.Core.Core.Abstractions;

public enum ModuleIdentifier
{
    AUTHENTICATION,
    MAIN,
    SETTINGS,
    CHATS,
}

public static class ModuleIdentifierExtensions
{
    public static string ToName(this ModuleIdentifier identifier) => identifier switch
    {
        ModuleIdentifier.AUTHENTICATION => "Authentication",
        ModuleIdentifier.MAIN => "Main",
        ModuleIdentifier.SETTINGS => "Settings",
        ModuleIdentifier.CHATS => "Chats",
        _ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "Unknown module identifier")
    };
}
