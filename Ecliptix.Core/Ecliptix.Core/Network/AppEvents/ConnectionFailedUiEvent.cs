namespace Ecliptix.Core.Network.AppEvents;

public record ConnectionFailedUiEvent(string Reason);

public record InitializationStatusUpdate(string Status);