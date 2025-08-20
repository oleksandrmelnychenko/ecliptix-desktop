using System;

namespace Ecliptix.Core.Core.Abstractions;

public enum ModuleIdentifier
{
    Authentication,
    Main,
    Settings,
    Chat,
    Files
}

public static class ModuleIdentifierExtensions
{
    public static string ToName(this ModuleIdentifier identifier) => identifier switch
    {
        ModuleIdentifier.Authentication => "Authentication",
        ModuleIdentifier.Main => "Main",
        ModuleIdentifier.Settings => "Settings",
        ModuleIdentifier.Chat => "Chat",
        ModuleIdentifier.Files => "Files",
        _ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "Unknown module identifier")
    };

    public static ModuleIdentifier FromName(string name) => name switch
    {
        "Authentication" => ModuleIdentifier.Authentication,
        "Main" => ModuleIdentifier.Main,
        "Settings" => ModuleIdentifier.Settings,
        "Chat" => ModuleIdentifier.Chat,
        "Files" => ModuleIdentifier.Files,
        _ => throw new ArgumentException($"Unknown module name: {name}", nameof(name))
    };
}