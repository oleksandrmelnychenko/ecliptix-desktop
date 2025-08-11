using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Services.Retry;

public static class FailureClassification
{
    public static bool IsTransient(NetworkFailure failure)
    {
        string message = failure.Message?.ToLowerInvariant() ?? string.Empty;

        if (message.Contains("timeout") ||
            message.Contains("temporarily") ||
            message.Contains("unavailable") ||
            message.Contains("not responding") ||
            message.Contains("connection reset") ||
            message.Contains("connection refused") ||
            message.Contains("recovering") ||
            message.Contains("network") ||
            message.Contains("retry") ||
            message.Contains("backoff"))
        {
            return true;
        }

        return !message.Contains("invalid request") &&
               !message.Contains("bad request") &&
               !message.Contains("unauthorized") &&
               !message.Contains("forbidden") &&
               !message.Contains("not found");
    }

    public static bool IsServerShutdown(NetworkFailure failure)
    {
        string msg = failure.Message?.ToLowerInvariant() ?? string.Empty;

        return msg.Contains("shutdown") ||
               msg.Contains("unavailable") ||
               msg.Contains("not responding") ||
               msg.Contains("service unavailable") ||
               msg.Contains("connection refused") ||
               msg.Contains("connection reset") ||
               msg.Contains("temporarily") ||
               msg.Contains("maintenance");
    }
    
    public static bool IsCryptoDesync(NetworkFailure failure)
    {
        string msg = failure.Message?.ToLowerInvariant() ?? string.Empty;

        return msg.Contains("decrypt failed") ||
               msg.Contains("desync") ||
               msg.Contains("rekey");
    }
    
    public static bool IsChainRotationMismatch(NetworkFailure failure)
    {
        string msg = failure.Message?.ToLowerInvariant() ?? string.Empty;

        return msg.Contains("requested index") && msg.Contains("not future") ||
               msg.Contains("chain rotation") ||
               msg.Contains("sequence mismatch") ||
               msg.Contains("protocol state") && msg.Contains("mismatch") ||
               msg.Contains("dhpublic") && msg.Contains("unknown") ||
               msg.Contains("sender chain") && msg.Contains("invalid") ||
               msg.Contains("receiver chain") && msg.Contains("invalid");
    }
    
    public static bool IsProtocolStateMismatch(NetworkFailure failure)
    {
        string msg = failure.Message?.ToLowerInvariant() ?? string.Empty;
        
        return IsChainRotationMismatch(failure) ||
               msg.Contains("protocol version") ||
               msg.Contains("state version") ||
               msg.Contains("channel state") && msg.Contains("invalid");
    }
    
    public static bool IsOutageRecoveryWait(NetworkFailure failure)
    {
        string msg = failure.Message?.ToLowerInvariant() ?? string.Empty;
        
        return msg.Contains("connection unavailable") && msg.Contains("server may be recovering");
    }
}
