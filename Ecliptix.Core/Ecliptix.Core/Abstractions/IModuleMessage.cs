namespace Ecliptix.Core.Modularity;

public interface IModuleMessage
{
    string MessageId { get; }

    string SourceModule { get; }

    string? TargetModule { get; }

    string MessageType { get; }

    string? CorrelationId { get; }
}

public abstract record ModuleMessage : IModuleMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string SourceModule { get; init; } = string.Empty;
    public string? TargetModule { get; init; }
    public abstract string MessageType { get; }
    public string? CorrelationId { get; init; }
}

public abstract record ModuleRequest : ModuleMessage
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public abstract record ModuleResponse : ModuleMessage
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }
}

public abstract record ModuleEvent : ModuleMessage
{
    public Dictionary<string, object> EventData { get; init; } = new();
}
