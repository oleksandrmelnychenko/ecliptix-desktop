namespace Ecliptix.Protocol.System.Security.ReplayProtection;

internal sealed class MessageWindow
{
    private readonly SortedSet<ulong> _processedIndices = [];
    private readonly DateTime _createdAt = DateTime.UtcNow;

    public ulong HighestProcessedIndex { get; private set; }

    public MessageWindow(ulong initialIndex)
    {
        HighestProcessedIndex = initialIndex;
        _processedIndices.Add(initialIndex);
    }

    public bool IsProcessed(ulong messageIndex) => _processedIndices.Contains(messageIndex);

    public void MarkProcessed(ulong messageIndex)
    {
        _processedIndices.Add(messageIndex);
        if (messageIndex > HighestProcessedIndex)
        {
            HighestProcessedIndex = messageIndex;
        }
    }

    public void CleanupOldEntries(DateTime cutoff)
    {
        if (_createdAt < cutoff)
        {
            ulong keepFromIndex = HighestProcessedIndex > 1000 ? HighestProcessedIndex - 1000 : 0;
            _processedIndices.RemoveWhere(idx => idx < keepFromIndex);
        }
    }
}
