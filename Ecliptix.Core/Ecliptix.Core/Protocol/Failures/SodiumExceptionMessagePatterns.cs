namespace Ecliptix.Core.Protocol.Failures;

internal static class SodiumExceptionMessagePatterns
{
    public const string SodiumInitPattern = "sodium_init() returned an error code";
    public const string AddressPinnedObjectPattern = "AddrOfPinnedObject returned IntPtr.Zero";
}