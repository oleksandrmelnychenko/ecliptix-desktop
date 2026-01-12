using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using Google.Protobuf;
using PubKeyExchangeType = Ecliptix.Protobuf.Protocol.PubKeyExchangeType;

namespace Ecliptix.Utilities;

public static class Helpers
{
    public static ByteString GuidToByteString(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);

        // Swap endianness in-place for first 4 bytes
        (bytes[0], bytes[1], bytes[2], bytes[3]) = (bytes[3], bytes[2], bytes[1], bytes[0]);
        // Swap bytes 4-5
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        // Swap bytes 6-7
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);

        return ByteString.CopyFrom(bytes);
    }

    public static Guid FromByteStringToGuid(ByteString byteString)
    {
        Span<byte> bytes = stackalloc byte[16];
        byteString.Span.CopyTo(bytes);

        // Swap endianness in-place for first 4 bytes
        (bytes[0], bytes[1], bytes[2], bytes[3]) = (bytes[3], bytes[2], bytes[1], bytes[0]);
        // Swap bytes 4-5
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        // Swap bytes 6-7
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);

        return new Guid(bytes);
    }

    public static T ParseFromBytes<T>(byte[] data)
    {

#pragma warning disable IL2090
        PropertyInfo? parserProperty = typeof(T).GetProperty("Parser");
#pragma warning restore IL2090
        if (parserProperty?.GetValue(null) is not { } parserInstance)
        {
            throw new InvalidOperationException($"No parser available for type {typeof(T).Name}");
        }

        MethodInfo? parseMethod = parserInstance.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
        if (parseMethod != null)
        {
            return (T)parseMethod.Invoke(parserInstance, [data])!;
        }

        throw new InvalidOperationException($"No parser available for type {typeof(T).Name}");
    }

    private static uint ComputeUniqueConnectId(
        ReadOnlySpan<byte> appInstanceId,
        ReadOnlySpan<byte> appDeviceId,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        int totalLength = appInstanceId.Length + appDeviceId.Length + sizeof(uint);
        if (operationContextId.HasValue)
        {
            totalLength += UtilityConstants.Cryptography.GUID_SIZE_BYTES;
        }

        Span<byte> buffer = totalLength <= UtilityConstants.Cryptography.STACK_ALLOC_THRESHOLD ? stackalloc byte[totalLength] : new byte[totalLength];

        int offset = 0;

        appInstanceId.CopyTo(buffer[offset..]);
        offset += appInstanceId.Length;

        appDeviceId.CopyTo(buffer[offset..]);
        offset += appDeviceId.Length;

        BinaryPrimitives.WriteUInt32BigEndian(buffer[offset..], (uint)contextType);
        offset += sizeof(uint);

        operationContextId?.TryWriteBytes(buffer[offset..]);
        Span<byte> hash = stackalloc byte[UtilityConstants.Cryptography.SHA_256_OUTPUT_SIZE];
        SHA256.TryHashData(buffer, hash, out _);

        return BinaryPrimitives.ReadUInt32BigEndian(hash[..UtilityConstants.Cryptography.HASH_BYTES_TO_READ]);
    }

    public static uint ComputeUniqueConnectId(
        string appInstanceIdString,
        string appDeviceIdString,
        PubKeyExchangeType contextType,
        Guid? operationContextId = null)
    {
        if (!Guid.TryParse(appInstanceIdString, out Guid appInstanceGuid))
        {
            throw new ArgumentException($"{UtilityConstants.ErrorMessages.INVALID_APP_INSTANCE_ID_FORMAT}{appInstanceIdString}");
        }

        if (!Guid.TryParse(appDeviceIdString, out Guid appDeviceGuid))
        {
            throw new ArgumentException($"{UtilityConstants.ErrorMessages.INVALID_APP_DEVICE_ID_FORMAT}{appDeviceIdString}");
        }

        Span<byte> appInstanceBytes = stackalloc byte[UtilityConstants.Cryptography.GUID_SIZE_BYTES];
        Span<byte> appDeviceBytes = stackalloc byte[UtilityConstants.Cryptography.GUID_SIZE_BYTES];

        appInstanceGuid.TryWriteBytes(appInstanceBytes);
        appDeviceGuid.TryWriteBytes(appDeviceBytes);

        return ComputeUniqueConnectId(
            appInstanceBytes,
            appDeviceBytes,
            contextType,
            operationContextId);
    }
}
