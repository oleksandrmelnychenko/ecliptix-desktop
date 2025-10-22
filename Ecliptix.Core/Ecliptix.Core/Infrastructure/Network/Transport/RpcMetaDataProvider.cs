using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;

namespace Ecliptix.Core.Infrastructure.Network.Transport;

internal sealed class RpcMetaDataProvider : IRpcMetaDataProvider
{
    public Guid AppInstanceId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string? Culture { get; private set; }
    public string LocalIpAddress { get; }
    public string? PublicIpAddress { get; }
    public string Platform { get; }

    public RpcMetaDataProvider()
    {
        LocalIpAddress = DetectLocalIpAddress();
        PublicIpAddress = DetectPublicIpAddress();
        Platform = DetectPlatform();
    }

    public void SetAppInfo(Guid appInstanceId, Guid deviceId, string? culture)
    {
        AppInstanceId = appInstanceId;
        DeviceId = deviceId;
        Culture = culture;
    }

    public void SetCulture(string? culture)
    {
        Culture = culture;
    }

    private static string DetectLocalIpAddress()
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

    private static string? DetectPublicIpAddress()
    {
        try
        {
            using UdpClient client = new();
            client.Connect("8.8.8.8", 80);
            IPEndPoint? endpoint = client.Client.LocalEndPoint as IPEndPoint;
            return endpoint?.Address.ToString() ?? DetectLocalIpAddress();
        }
        catch
        {
            return DetectLocalIpAddress();
        }
    }

    private static string DetectPlatform()
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
