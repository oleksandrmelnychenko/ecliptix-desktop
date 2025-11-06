namespace Ecliptix.Protocol.System.Security.ReplayProtection;

internal readonly struct NonceKey(byte[] nonce) : IEquatable<NonceKey>
{
    private readonly byte[] _nonce = (byte[])nonce.Clone();
    private readonly int _hashCode = ComputeHashCode(nonce);

    private static int ComputeHashCode(ReadOnlySpan<byte> nonce)
    {
        HashCode hash = new();
        hash.AddBytes(nonce);
        return hash.ToHashCode();
    }

    public bool Equals(NonceKey other) => _nonce.AsSpan().SequenceEqual(other._nonce);

    public override bool Equals(object? obj) => obj is NonceKey other && Equals(other);

    public override int GetHashCode() => _hashCode;
}
