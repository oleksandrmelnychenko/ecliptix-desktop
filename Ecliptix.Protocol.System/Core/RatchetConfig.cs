namespace Ecliptix.Protocol.System.Core;

public sealed class RatchetConfig
{
    public static readonly RatchetConfig Default = new();

    public static readonly RatchetConfig Conservative = new()
    {
        DhRatchetEveryNMessages = 50,
        EnablePerMessageRatchet = false
    };

    public static readonly RatchetConfig Aggressive = new()
    {
        DhRatchetEveryNMessages = 5,
        EnablePerMessageRatchet = false
    };

    public static readonly RatchetConfig PerMessage = new()
    {
        DhRatchetEveryNMessages = 1,
        EnablePerMessageRatchet = true
    };

    public uint DhRatchetEveryNMessages { get; init; } = 10;

    public bool EnablePerMessageRatchet { get; init; } = false;

    public bool RatchetOnNewDhKey { get; init; } = true;

    public TimeSpan MaxChainAge { get; init; } = ProtocolSystemConstants.Timeouts.DefaultMaxChainAge;

    public uint MaxMessagesWithoutRatchet { get; init; } = 1000;

    public bool ShouldRatchet(uint messageIndex, DateTime lastRatchetTime, bool receivedNewDhKey, DateTime currentTime)
    {
        if (EnablePerMessageRatchet)
            return true;

        if (RatchetOnNewDhKey && receivedNewDhKey)
            return true;

        if (messageIndex > 0 && messageIndex % DhRatchetEveryNMessages == 0)
            return true;

        if (currentTime - lastRatchetTime > MaxChainAge)
            return true;

        if (messageIndex >= MaxMessagesWithoutRatchet)
            return true;

        return false;
    }

    public bool ShouldRatchet(uint messageIndex, DateTime lastRatchetTime, bool receivedNewDhKey)
    {
        return ShouldRatchet(messageIndex, lastRatchetTime, receivedNewDhKey, DateTime.UtcNow);
    }
}