using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Communication;
public record ModuleInitializedEvent : ModuleEvent
{
    public override string MessageType => "module.initialized";
    public string ModuleName { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
}

public record UserAuthenticatedEvent : ModuleEvent
{
    public override string MessageType => "user.authenticated";
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
}

public record UserSignedOutEvent : ModuleEvent
{
    public override string MessageType => "user.signed_out";
    public string UserId { get; init; } = string.Empty;
}
public record NavigationRequestedEvent : ModuleEvent
{
    public override string MessageType => "navigation.requested";
    public string TargetView { get; init; } = string.Empty;
    public object? NavigationParameters { get; init; }
}