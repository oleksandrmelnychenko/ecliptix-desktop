using System.Buffers.Binary;
using System.Security.Cryptography;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Google.Protobuf;

namespace Ecliptix.Utilities;

public static class Helpers
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static uint GenerateRandomUInt32(bool excludeZero = false)
    {
        byte[] buffer = new byte[UtilityConstants.Cryptography.UInt32SizeBytes];
        uint value;
        int attempts = 0;
        do
        {
            Rng.GetBytes(buffer);
            value = BitConverter.ToUInt32(buffer, 0);

            if (++attempts > UtilityConstants.Cryptography.MaxEntropyCheckAttempts && IsLowEntropy(buffer))
            {
                throw new InvalidOperationException(UtilityConstants.ErrorMessages.InsufficientEntropy);
            }
        } while (excludeZero && value == 0);

        return value;
    }

    private static bool IsLowEntropy(byte[] data)
    {
        if (data.All(b => b == UtilityConstants.Cryptography.MinByteValue) || data.All(b => b == UtilityConstants.Cryptography.MaxByteValue))
            return true;

        if (data.All(b => b == data[0]))
            return true;

        bool isSequential = true;
        for (int i = 1; i < data.Length && isSequential; i++)
        {
            if (data[i] != (byte)(data[i - 1] + 1))
                isSequential = false;
        }

        return isSequential;
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
        byte[] bytesOriginal = byteString.ToByteArray();
        byte[] bytes = (byte[])bytesOriginal.Clone();

        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);

        Guid result = new Guid(bytes);

        return result;
    }

    public static uint GenerateRandomUInt32InRange(uint min, uint max)
    {
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        byte[] bytes = new byte[UtilityConstants.Cryptography.UInt32SizeBytes];
        rng.GetBytes(bytes);
        uint value = BitConverter.ToUInt32(bytes, 0);
        return min + value % (max - min + 1);
    }

    public static T ParseFromBytes<T>(byte[] data) where T : IMessage<T>, new()
    {
        MessageParser<T> parser = new(() => new T());
        return parser.ParseFrom(data);
    }

    private static uint ComputeUniqueConnectId(
        ReadOnlySpan<byte> appInstanceId,
        ReadOnlySpan<byte> appDeviceId,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        int totalLength = appInstanceId.Length + appDeviceId.Length + sizeof(uint);
        if (operationContextId.HasValue)
            totalLength += UtilityConstants.Cryptography.GuidSizeBytes;

        Span<byte> buffer = totalLength <= UtilityConstants.Cryptography.StackAllocThreshold ? stackalloc byte[totalLength] : new byte[totalLength];

        int offset = 0;

        appInstanceId.CopyTo(buffer[offset..]);
        offset += appInstanceId.Length;

        appDeviceId.CopyTo(buffer[offset..]);
        offset += appDeviceId.Length;

        BinaryPrimitives.WriteUInt32BigEndian(buffer[offset..], (uint)contextType);
        offset += sizeof(uint);

        operationContextId?.TryWriteBytes(buffer[offset..]);
        Span<byte> hash = stackalloc byte[UtilityConstants.Cryptography.Sha256OutputSize];
        SHA256.TryHashData(buffer, hash, out _);

        return BinaryPrimitives.ReadUInt32BigEndian(hash[..UtilityConstants.Cryptography.HashBytesToRead]);
    }

    public static uint ComputeUniqueConnectId(
        string appInstanceIdString,
        string appDeviceIdString,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        if (!Guid.TryParse(appInstanceIdString, out Guid appInstanceGuid))
            throw new ArgumentException($"{UtilityConstants.ErrorMessages.InvalidAppInstanceIdFormat}{appInstanceIdString}");

        if (!Guid.TryParse(appDeviceIdString, out Guid appDeviceGuid))
            throw new ArgumentException($"{UtilityConstants.ErrorMessages.InvalidAppDeviceIdFormat}{appDeviceIdString}");

        Span<byte> appInstanceBytes = stackalloc byte[UtilityConstants.Cryptography.GuidSizeBytes];
        Span<byte> appDeviceBytes = stackalloc byte[UtilityConstants.Cryptography.GuidSizeBytes];

        appInstanceGuid.TryWriteBytes(appInstanceBytes);
        appDeviceGuid.TryWriteBytes(appDeviceBytes);

        return ComputeUniqueConnectId(
            appInstanceBytes,
            appDeviceBytes,
            contextType,
            operationContextId);
    }

}
