namespace Ecliptix.Core.Modularity.Messaging;

public record ModuleInitializedEvent : ModuleEvent
{
    public override string MessageType => "module.initialized";
    public string ModuleName { get; init; } = string.Empty;
}
