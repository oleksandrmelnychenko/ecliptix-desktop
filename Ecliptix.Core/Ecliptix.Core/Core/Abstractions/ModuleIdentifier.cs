using System;

namespace Ecliptix.Core.Core.Abstractions;

public enum ModuleIdentifier
{
    Authentication,
    Main,
    Settings,
}

public static class ModuleIdentifierExtensions
{
    public static string ToName(this ModuleIdentifier identifier) => identifier switch
    {
        ModuleIdentifier.Authentication => "Authentication",
        ModuleIdentifier.Main => "Main",
        ModuleIdentifier.Settings => "Settings",
        _ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "Unknown module identifier")
    };
}
