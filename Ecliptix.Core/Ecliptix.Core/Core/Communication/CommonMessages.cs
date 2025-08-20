using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Communication;
public record ModuleInitializedEvent : ModuleEvent
{
    public override string MessageType => "module.initialized";
    public string ModuleName { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
}

public record ModuleShuttingDownEvent : ModuleEvent
{
    public override string MessageType => "module.shutting_down";
    public string ModuleName { get; init; } = string.Empty;
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

public record ViewChangedEvent : ModuleEvent
{
    public override string MessageType => "view.changed";
    public string PreviousView { get; init; } = string.Empty;
    public string CurrentView { get; init; } = string.Empty;
}
public record ConfigurationChangedEvent : ModuleEvent
{
    public override string MessageType => "configuration.changed";
    public string ConfigKey { get; init; } = string.Empty;
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}
public record ResourceLimitReachedEvent : ModuleEvent
{
    public override string MessageType => "resource.limit_reached";
    public string ResourceType { get; init; } = string.Empty;
    public long CurrentUsage { get; init; }
    public long Limit { get; init; }
}
public record GetConfigurationRequest : ModuleRequest
{
    public override string MessageType => "configuration.get";
    public string ConfigKey { get; init; } = string.Empty;
}

public record GetConfigurationResponse : ModuleResponse
{
    public override string MessageType => "configuration.get_response";
    public string ConfigKey { get; init; } = string.Empty;
    public object? Value { get; init; }
}

public record ModuleHealthCheckRequest : ModuleRequest
{
    public override string MessageType => "module.health_check";
}

public record ModuleHealthCheckResponse : ModuleResponse
{
    public override string MessageType => "module.health_check_response";
    public string ModuleName { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string? Details { get; init; }
}
public record ValidateInputRequest : ModuleRequest
{
    public override string MessageType => "validation.validate_input";
    public string InputType { get; init; } = string.Empty;
    public object? InputValue { get; init; }
}

public record ValidateInputResponse : ModuleResponse
{
    public override string MessageType => "validation.validate_input_response";
    public bool IsValid { get; init; }
    public string[]? ValidationErrors { get; init; }
}