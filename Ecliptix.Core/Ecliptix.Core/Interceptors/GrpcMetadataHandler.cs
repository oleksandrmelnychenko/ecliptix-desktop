using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using System;
using System.Security.Cryptography;
using Grpc.Core;
using System.Globalization;

namespace Ecliptix.Core.Interceptors;

public static class GrpcMetadataHandler
{
    private const string RequestIdKey = "request-id";
    private const string DateTimeKey = "request-date";
    private const string LocalIpAddressKey = "local-ip-address";
    private const string PublicIpAddressKey = "public-ip-address";
    private const string LocaleKey = "lang";
    private const string LinkIdKey = "fetch-link";
    private const string ApiKey = "api-key";
    private const string AppDeviceId = "d-identifier";
    private const string KeyExchangeContextTypeKey = "oiwfT6c5kOQsZozxhTBg";
    private const string KeyExchangeContextTypeValue = "JmTGdGilMka07zyg5hz6Q";
    private const string ConnectionContextId = "c-context-id";
    private const string OperationContextId = "o-context-id";

    // Generate metadata for a gRPC request
    public static Metadata GenerateMetadata()
    {
        var metadata = new Metadata
        {
            { RequestIdKey, Guid.NewGuid().ToString() },
            { DateTimeKey, DateTimeOffset.UtcNow.ToString("O") },
            { LocalIpAddressKey, GetLocalIpAddress() },
            { PublicIpAddressKey, GetPublicIpAddress() },
            { LocaleKey, CultureInfo.CurrentCulture.Name },
            { LinkIdKey, "fetch-link-placeholder" }, // Replace with actual link ID
            { ApiKey, "your-api-key" }, // Replace with actual API key
            { AppDeviceId, Guid.NewGuid().ToString() }, // Replace with device-specific ID
            { KeyExchangeContextTypeKey, KeyExchangeContextTypeValue },
            { ConnectionContextId, PubKeyExchangeType.AppDeviceEphemeralConnect.ToString() },
            { OperationContextId, Guid.NewGuid().ToString() } // Optional operation context ID
        };

        return metadata;
    }

    // Compute unique connect ID
    public static Result<uint, string> ComputeUniqueConnectId(Metadata metadata)
    {
        try
        {
            string appDeviceId = metadata.GetValue(AppDeviceId)
                                 ?? throw new ArgumentException($"Component not found: {AppDeviceId}");
            Guid appDeviceGuid = Guid.Parse(appDeviceId);

            string connectionContextId = metadata.GetValue(ConnectionContextId)
                                         ?? throw new ArgumentException($"Component not found: {ConnectionContextId}");
            if (!Enum.TryParse<PubKeyExchangeType>(connectionContextId, true, out var contextType))
                throw new ArgumentException($"Invalid PubKeyExchangeType for key: {ConnectionContextId}");

            string? operationContextId = metadata.GetValue(OperationContextId);
            Guid? opContextGuid = operationContextId != null ? Guid.Parse(operationContextId) : null;

            byte[] guidBytes = appDeviceGuid.ToByteArray();
            byte[] contextBytes = BitConverter.GetBytes((uint)contextType);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(contextBytes);

            int totalLength = guidBytes.Length + contextBytes.Length + (opContextGuid.HasValue ? 16 : 0);
            byte[] combined = new byte[totalLength];
            Buffer.BlockCopy(guidBytes, 0, combined, 0, guidBytes.Length);
            Buffer.BlockCopy(contextBytes, 0, combined, guidBytes.Length, contextBytes.Length);
            if (opContextGuid.HasValue)
            {
                byte[] opContextBytes = opContextGuid.Value.ToByteArray();
                Buffer.BlockCopy(opContextBytes, 0, combined, guidBytes.Length + contextBytes.Length,
                    opContextBytes.Length);
            }

            byte[] hash = SHA256.HashData(combined);
            return Result<uint, string>.Ok(BitConverter.ToUInt32(hash, 0));
        }
        catch (Exception ex)
        {
            return Result<uint, string>.Err($"Failed to compute UniqueConnectId: {ex.Message}");
        }
    }

    // Placeholder for local IP address
    private static string GetLocalIpAddress()
    {
        return "127.0.0.1"; // Replace with actual IP retrieval logic
    }

    // Placeholder for public IP address
    private static string GetPublicIpAddress()
    {
        return "192.168.1.1"; // Replace with actual IP retrieval logic
    }
}

