using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ecliptix.Protobuf.Protocol;
using Grpc.Core;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;

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

    public static Metadata GenerateMetadata(string appInstanceId, string appDeviceId, string? culture,
        PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect,
        Guid operationId = default)
    {
        Metadata metadata = new()
        {
            { RequestIdKey, Guid.NewGuid().ToString() },
            { DateTimeKey, DateTimeOffset.UtcNow.ToString("O") },
            { LocalIpAddressKey, GetLocalIpAddress() },
            { PublicIpAddressKey, GetPublicIpAddress() },
            { LocaleKey, culture ?? "en-US" },
            { LinkIdKey, GenerateLinkId() },
            { ApplicationInstanceIdKey, appInstanceId },
            { AppDeviceId, appDeviceId },
            { KeyExchangeContextTypeKey, KeyExchangeContextTypeValue },
            { ConnectionContextId, exchangeType.ToString() },
            { OperationContextId, string.Empty }
        };

        return metadata;
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            string localIp = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up && !x.IsReceiveOnly)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address))
                .Select(x => x.Address.ToString())
                .FirstOrDefault() ?? "127.0.0.1";

            return localIp;
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string GetPublicIpAddress()
    {
        try
        {
            using UdpClient client = new();
            client.Connect("8.8.8.8", 80);
            IPEndPoint? endpoint = client.Client.LocalEndPoint as IPEndPoint;
            return endpoint?.Address.ToString() ?? GetLocalIpAddress();
        }
        catch
        {
            return GetLocalIpAddress();
        }
    }

    private static string GenerateLinkId()
    {
        return $"link-{Guid.NewGuid():N}"[..16];
    }
}
