using System.Buffers;
using Ecliptix.Protocol.System.Interfaces;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Models.Keys;
internal sealed class RatchetChainKey : IEquatable<RatchetChainKey>
{
    private readonly IKeyProvider _keyProvider;

    internal RatchetChainKey(uint index, IKeyProvider keyProvider)
    {
        Index = index;
        _keyProvider = keyProvider;
    }

    public uint Index { get; }

    public bool Equals(RatchetChainKey? other)
    {
        if (other is null)
        {
            return false;
        }

        return Index == other.Index && ReferenceEquals(_keyProvider, other._keyProvider);
    }

    public override bool Equals(object? obj)
    {
        return obj is RatchetChainKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Index.GetHashCode();
    }

    public static bool operator ==(RatchetChainKey? left, RatchetChainKey? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(RatchetChainKey? left, RatchetChainKey? right)
    {
        return !(left == right);
    }

    public static Result<Unit, EcliptixProtocolFailure> ReadKeyMaterial(RatchetChainKey chainKey, Span<byte> destination)
    {
        if (destination.Length < Constants.X_25519_KEY_SIZE)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.BUFFER_TOO_SMALL(
                    $"Destination buffer must be at least {Constants.X_25519_KEY_SIZE} bytes, but was {destination.Length}."));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Constants.X_25519_KEY_SIZE);
        try
        {
            Result<Unit, EcliptixProtocolFailure> result = chainKey.WithKeyMaterial<Unit>(keyMaterial =>
            {
                keyMaterial[..Constants.X_25519_KEY_SIZE].CopyTo(buffer);
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            });

            if (result.IsOk)
            {
                buffer.AsSpan(0, Constants.X_25519_KEY_SIZE).CopyTo(destination);
            }

            return result;
        }
        finally
        {
            Array.Clear(buffer, 0, Constants.X_25519_KEY_SIZE);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Result<T, EcliptixProtocolFailure> WithKeyMaterial<T>(Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation)
    {
        return _keyProvider.ExecuteWithKey(Index, operation);
    }
}
