using System.Buffers.Binary;
using System.Security.Cryptography;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;

namespace Ecliptix.Utilities;

public static class Helpers
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static uint GenerateRandomUInt32(bool excludeZero = false)
    {
        byte[] buffer = new byte[sizeof(uint)];
        uint value;
        do
        {
            Rng.GetBytes(buffer);
            value = BitConverter.ToUInt32(buffer, 0);
        } while (excludeZero && value == 0);

        return value;
    }

    public static byte[] GenerateSecureRandomTag(int tagLengthBytes)
    {
        if (tagLengthBytes < 1) throw new ArgumentOutOfRangeException(nameof(tagLengthBytes));
        byte[] tagBytes = new byte[tagLengthBytes];
        Rng.GetBytes(tagBytes);
        return tagBytes;
    }

    internal static void GenerateSecureRandomTag(Span<byte> destination)
    {
        if (destination.IsEmpty) throw new ArgumentException(null, nameof(destination));
        Rng.GetBytes(destination);
    }

    public static ByteString GuidToByteString(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];

        guid.TryWriteBytes(bytes);

        SwapBytes(bytes, 0, 3);
        SwapBytes(bytes, 1, 2);
        SwapBytes(bytes, 4, 5);
        SwapBytes(bytes, 6, 7);

        return ByteString.CopyFrom(bytes);
    }

    public static Guid FromByteStringToGuid(ByteString byteString)
    {
        if (byteString.Length != 16)
            throw new ArgumentException("ByteString must be 16 bytes long.", nameof(byteString));

        Span<byte> bytes = stackalloc byte[16];

        byte[] tempArray = new byte[16];
        byteString.CopyTo(tempArray, 0);
        tempArray.CopyTo(bytes);

        SwapBytes(bytes, 0, 3);
        SwapBytes(bytes, 1, 2);
        SwapBytes(bytes, 4, 5);
        SwapBytes(bytes, 6, 7);

        return new Guid(bytes);
    }

    private static void SwapBytes(Span<byte> bytes, int i, int j)
    {
        (bytes[i], bytes[j]) = (bytes[j], bytes[i]);
    }

    public static uint GenerateRandomUInt32InRange(uint min, uint max)
    {
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] bytes = new byte[4];
        rng.GetBytes(bytes);
        uint value = BitConverter.ToUInt32(bytes, 0);
        return min + value % (max - min + 1);
    }

    public static T ParseFromBytes<T>(byte[] data) where T : IMessage<T>, new()
    {
        MessageParser<T> parser = new(() => new T());
        return parser.ParseFrom(data);
    }

    public static uint ComputeUniqueConnectId(
        ReadOnlySpan<byte> appInstanceId,
        ReadOnlySpan<byte> appDeviceId,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        int totalLength = appInstanceId.Length + appDeviceId.Length + sizeof(uint);
        if (operationContextId.HasValue)
            totalLength += 16;

        Span<byte> buffer = totalLength <= 512 ? stackalloc byte[totalLength] : new byte[totalLength];

        int offset = 0;

        appInstanceId.CopyTo(buffer[offset..]);
        offset += appInstanceId.Length;

        appDeviceId.CopyTo(buffer[offset..]);
        offset += appDeviceId.Length;

        BinaryPrimitives.WriteUInt32BigEndian(buffer[offset..], (uint)contextType);
        offset += sizeof(uint);

        operationContextId?.TryWriteBytes(buffer[offset..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.TryHashData(buffer, hash, out _);

        return BinaryPrimitives.ReadUInt32BigEndian(hash[..4]);
    }

    /// <summary>
    /// Computes ConnectId using Guid strings (consistent with server-side calculation)
    /// </summary>
    public static uint ComputeUniqueConnectId(
        string appInstanceIdString,
        string appDeviceIdString,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        // Parse strings to Guid and use ToByteArray() to match server-side calculation
        if (!Guid.TryParse(appInstanceIdString, out Guid appInstanceGuid))
            throw new ArgumentException($"Invalid AppInstanceId format: {appInstanceIdString}");

        if (!Guid.TryParse(appDeviceIdString, out Guid appDeviceGuid))
            throw new ArgumentException($"Invalid AppDeviceId format: {appDeviceIdString}");

        byte[] appInstanceBytes = appInstanceGuid.ToByteArray();
        byte[] appDeviceBytes = appDeviceGuid.ToByteArray();

        // Use existing method with consistent byte arrays
        return ComputeUniqueConnectId(
            appInstanceBytes.AsSpan(),
            appDeviceBytes.AsSpan(),
            contextType,
            operationContextId);
    }


    public static uint GenerateRandomUInt32()
    {
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] bytes = new byte[4];
        rng.GetBytes(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }
}