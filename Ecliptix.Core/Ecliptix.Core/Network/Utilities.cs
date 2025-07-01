using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;

namespace Ecliptix.Core.Network;

public static class Utilities
{
    private const string InvalidPayloadDataLengthMessage = "Invalid payload data length.";

    public static byte[] ReadMemoryToRetrieveBytes(ReadOnlyMemory<byte> readOnlyMemory)
    {
        if (!MemoryMarshal.TryGetArray(readOnlyMemory, out ArraySegment<byte> segment) || segment.Count == 0)
            throw new ArgumentException(InvalidPayloadDataLengthMessage);

        return segment.Array!;
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

    public static async Task<byte[]> ExtractCipherPayload(ByteString requestedEncryptedPayload, string connectionId,
        Func<byte[], string, int, Task<byte[]>> decryptPayloadFun)
    {
        byte[] encryptedPayload = ReadMemoryToRetrieveBytes(requestedEncryptedPayload.Memory);
        return await decryptPayloadFun(encryptedPayload, connectionId, 0);
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

    public static uint GenerateRandomUInt32()
    {
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] bytes = new byte[4];
        rng.GetBytes(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }
}