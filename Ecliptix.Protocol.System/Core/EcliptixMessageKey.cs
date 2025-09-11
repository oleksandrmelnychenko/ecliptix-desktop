using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

internal interface IKeyProvider
{
    Result<T, EcliptixProtocolFailure> ExecuteWithKey<T>(uint keyIndex, Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation);
}

public sealed class EcliptixMessageKey : IEquatable<EcliptixMessageKey>
{
    private readonly IKeyProvider _keyProvider;

    internal EcliptixMessageKey(uint index, IKeyProvider keyProvider)
    {
        Index = index;
        _keyProvider = keyProvider;
    }

    public uint Index { get; }

    public bool Equals(EcliptixMessageKey? other)
    {
        if (other is null) return false;
        return Index == other.Index && ReferenceEquals(_keyProvider, other._keyProvider);
    }

    public Result<T, EcliptixProtocolFailure> WithKeyMaterial<T>(Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation)
    {
        return _keyProvider.ExecuteWithKey(Index, operation);
    }

    public Result<Unit, EcliptixProtocolFailure> ReadKeyMaterial(Span<byte> destination)
    {
        if (destination.Length < Constants.X25519KeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.BufferTooSmall(
                    $"Destination buffer must be at least {Constants.X25519KeySize} bytes, but was {destination.Length}."));

        byte[] buffer = new byte[Constants.X25519KeySize];
        Result<Unit, EcliptixProtocolFailure> result = WithKeyMaterial<Unit>(keyMaterial =>
        {
            keyMaterial[..Constants.X25519KeySize].CopyTo(buffer);
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        });

        if (result.IsOk)
        {
            buffer.CopyTo(destination);
        }
        
        return result;
    }

    public override bool Equals(object? obj)
    {
        return obj is EcliptixMessageKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Index.GetHashCode();
    }

    public static bool operator ==(EcliptixMessageKey? left, EcliptixMessageKey? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(EcliptixMessageKey? left, EcliptixMessageKey? right)
    {
        return !(left == right);
    }
}