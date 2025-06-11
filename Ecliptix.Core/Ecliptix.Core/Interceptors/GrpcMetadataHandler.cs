using System;
using System.Globalization;
using Ecliptix.Protobuf.PubKeyExchange;
using Grpc.Core;

namespace Ecliptix.Core.Interceptors;

public static class GrpcMetadataHandler
{
    private const string RequestIdKey = "request-id";
    private const string DateTimeKey = "request-date";
    private const string LocalIpAddressKey = "local-ip-address";
    private const string PublicIpAddressKey = "public-ip-address";
    private const string LocaleKey = "lang";
    private const string LinkIdKey = "fetch-link";
    private const string ApplicationInstanceIdKey = "application-identifier";
    private const string AppDeviceId = "d-identifier";
    private const string KeyExchangeContextTypeKey = "oiwfT6c5kOQsZozxhTBg";
    private const string KeyExchangeContextTypeValue = "JmTGdGilMka07zyg5hz6Q";
    private const string ConnectionContextId = "c-context-id";
    private const string OperationContextId = "o-context-id";

    public static Metadata GenerateMetadata(Guid appInstanceId, Guid appDeviceId, Guid operationId = default)
    {
        Metadata metadata = new()
        {
            { RequestIdKey, Guid.NewGuid().ToString() },
            { DateTimeKey, DateTimeOffset.UtcNow.ToString("O") },
            { LocalIpAddressKey, GetLocalIpAddress() },
            { PublicIpAddressKey, GetPublicIpAddress() },
            { LocaleKey, "uk-ua" },
            { LinkIdKey, "fetch-link-placeholder" },
            { ApplicationInstanceIdKey, appInstanceId.ToString() },
            { AppDeviceId, appDeviceId.ToString() },
            { KeyExchangeContextTypeKey, KeyExchangeContextTypeValue },
            { ConnectionContextId, nameof(PubKeyExchangeType.DataCenterEphemeralConnect) },
            { OperationContextId, string.Empty }
        };

        return metadata;
    }

    private static string GetLocalIpAddress()
    {
        return "127.0.0.1";
    }

    private static string GetPublicIpAddress()
    {
        return "192.168.1.1";
    }
}