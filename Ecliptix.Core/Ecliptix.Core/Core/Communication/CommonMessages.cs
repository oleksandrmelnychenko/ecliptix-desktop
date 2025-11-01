using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Communication;
public record ModuleInitializedEvent : ModuleEvent
{
    public override string MessageType => "module.initialized";
    public string ModuleName { get; init; } = string.Empty;
}
