using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;

namespace Ecliptix.Core.Network;

public static class ServiceUtilities
{
    private const string InvalidPayloadDataLengthMessage = "Invalid payload data length.";

    public static byte[] ReadMemoryToRetrieveBytes(ReadOnlyMemory<byte> readOnlyMemory)
    {
        if (!MemoryMarshal.TryGetArray(readOnlyMemory, out ArraySegment<byte> segment) || segment.Count == 0)
        {
            throw new ArgumentException(InvalidPayloadDataLengthMessage);
        }

        return segment.Array!;
    }

    public static ByteString GuidToByteString(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
        return ByteString.CopyFrom(bytes);
    }

    public static Guid FromByteStringToGuid(ByteString byteString)
    {
        byte[] bytes = byteString.ToByteArray();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        return new Guid(bytes);
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
        Guid appInstanceId,
        Guid appDeviceId,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        byte[] appInstanceIdBytes = appInstanceId.ToByteArray();
        byte[] appDeviceIdBytes = appDeviceId.ToByteArray();
        uint contextTypeUint = (uint)contextType;
        byte[] contextTypeBytes = BitConverter.GetBytes(contextTypeUint);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(contextTypeBytes);
        }

        int totalLength = appInstanceIdBytes.Length + appDeviceIdBytes.Length + contextTypeBytes.Length;
        if (operationContextId.HasValue)
        {
            totalLength += 16;
        }

        byte[] combined = new byte[totalLength];
        int offset = 0;
        Buffer.BlockCopy(appInstanceIdBytes, 0, combined, offset, appInstanceIdBytes.Length);
        offset += appInstanceIdBytes.Length;
        Buffer.BlockCopy(appDeviceIdBytes, 0, combined, offset, appDeviceIdBytes.Length);
        offset += appDeviceIdBytes.Length;
        Buffer.BlockCopy(contextTypeBytes, 0, combined, offset, contextTypeBytes.Length);
        offset += contextTypeBytes.Length;
        if (operationContextId.HasValue)
        {
            byte[] opContextBytes = operationContextId.Value.ToByteArray();
            Buffer.BlockCopy(opContextBytes, 0, combined, offset, opContextBytes.Length);
        }

        byte[] hash = SHA256.HashData(combined);
        return BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(0, 4));
    }

    public static uint GenerateRandomUInt32()
    {
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] bytes = new byte[4];
        rng.GetBytes(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }
}