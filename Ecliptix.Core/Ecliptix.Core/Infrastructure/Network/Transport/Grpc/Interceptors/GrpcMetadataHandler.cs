using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Ecliptix.Protobuf.Protocol;
using Grpc.Core;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;

public static class GrpcMetadataHandler
{
    private const string REQUEST_ID_KEY = "request-id";
    private const string DATE_TIME_KEY = "request-date";
    private const string LOCAL_IP_ADDRESS_KEY = "local-ip-address";
    private const string PUBLIC_IP_ADDRESS_KEY = "public-ip-address";
    private const string LOCALE_KEY = "lang";
    private const string LINK_ID_KEY = "fetch-link";
    private const string APPLICATION_INSTANCE_ID_KEY = "application-identifier";
    private const string APP_DEVICE_ID = "d-identifier";
    private const string PLATFORM_KEY = "platform";
    private const string KEY_EXCHANGE_CONTEXT_TYPE_KEY = "oiwfT6c5kOQsZozxhTBg";
    private const string KEY_EXCHANGE_CONTEXT_TYPE_VALUE = "JmTGdGilMka07zyg5hz6Q";
    private const string CONNECTION_CONTEXT_ID = "c-context-id";
    private const string OPERATION_CONTEXT_ID = "o-context-id";

    public static Metadata GenerateMetadata(
        string appInstanceId,
        string appDeviceId,
        string? culture,
        PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect,
        string? localIpAddress = null,
        string? publicIpAddress = null,
        string? platform = null)
    {
        Metadata metadata = new()
        {
            { REQUEST_ID_KEY, Guid.NewGuid().ToString() },
            { DATE_TIME_KEY, DateTimeOffset.UtcNow.ToString("O") },
            { LOCAL_IP_ADDRESS_KEY, localIpAddress ?? GetLocalIpAddress() },
            { PUBLIC_IP_ADDRESS_KEY, publicIpAddress ?? GetPublicIpAddress() },
            { LOCALE_KEY, culture ?? "en-US" },
            { LINK_ID_KEY, GenerateLinkId() },
            { APPLICATION_INSTANCE_ID_KEY, appInstanceId },
            { APP_DEVICE_ID, appDeviceId },
            { PLATFORM_KEY, platform ?? GetPlatform() },
            { KEY_EXCHANGE_CONTEXT_TYPE_KEY, KEY_EXCHANGE_CONTEXT_TYPE_VALUE },
            { CONNECTION_CONTEXT_ID, exchangeType.ToString() },
            { OPERATION_CONTEXT_ID, string.Empty }
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

    private static string GenerateLinkId() => $"link-{Guid.NewGuid():N}"[..16];

    private static string GetPlatform()
    {
        string os = GetOperatingSystem();
        string arch = GetArchitecture();
        string runtime = GetRuntimeVersion();
        return $"{os}-{arch} (.NET {runtime})";
    }

    private static string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return "Unknown";
    }

    private static string GetArchitecture()
    {
        Architecture processArch = RuntimeInformation.ProcessArchitecture;
        return processArch switch
        {
            Architecture.Arm64 => "ARM64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "ARM32",
            _ => processArch.ToString()
        };
    }

    private static string GetRuntimeVersion()
    {
        string frameworkDescription = RuntimeInformation.FrameworkDescription;
        int startIndex = frameworkDescription.IndexOf(' ');
        if (startIndex >= 0 && startIndex < frameworkDescription.Length - 1)
        {
            return frameworkDescription[(startIndex + 1)..];
        }
        return frameworkDescription;
    }
}
