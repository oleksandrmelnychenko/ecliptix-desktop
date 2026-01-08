namespace Ecliptix.Protected.Protocol.Utilities;

internal static class SecureArrayPool
{
    public static SecurePooledArray<T> Rent<T>(int minimumLength) where T : struct => new(minimumLength);
}
