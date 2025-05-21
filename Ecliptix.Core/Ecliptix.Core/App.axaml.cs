using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Authentication;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Splat;

namespace Ecliptix.Core;

public class App : Application
{
    private const int DefaultOneTimeKeyCount = 10;
    private readonly Lock _lock = new();
    private readonly ILogger<App> _logger = Locator.Current.GetService<ILogger<App>>()!;
    private readonly NetworkController _networkController = Locator.Current.GetService<NetworkController>()!;

    private (uint, AppDevice) CreateEcliptixConnectionContext()
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;
        AppDevice appDevice = new()
        {
            AppInstanceId = Utilities.GuidToByteString(appInstanceInfo.AppInstanceId),
            DeviceId = Utilities.GuidToByteString(appInstanceInfo.DeviceId),
            DeviceType = AppDevice.Types.DeviceType.Desktop
        };

        uint connectId = Utilities.ComputeUniqueConnectId(
            appInstanceInfo.AppInstanceId,
            appInstanceInfo.DeviceId,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        _networkController.CreateEcliptixConnectionContext(connectId, DefaultOneTimeKeyCount,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        return (connectId, appDevice);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        _ = InitializeApplicationAsync();

        AppSettings? appSettings = Locator.Current.GetService<AppSettings>();
        if (appSettings == null)
        {
            // TODO: Load store to get the settings.
        }

        const bool isAuthorized = false;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new AuthenticationWindow
            {
               DataContext = Locator.Current.GetService<AuthenticationViewModel>()
            };
            
            /*AuthenticationViewModel authViewModel =
                Locator.Current.GetService<AuthenticationViewModel>()!;
            desktop.MainWindow = new AuthenticationWindow
            {
                DataContext = authViewModel
            };*/
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = Locator.Current.GetService<MainViewModel>()
            };
        }
    }

    private async Task InitializeApplicationAsync()
    {
        (uint connectId, AppDevice appDevice) = CreateEcliptixConnectionContext();

        Result<Unit, ShieldFailure> result = await _networkController.DataCenterPubKeyExchange(connectId);
        if (result.IsErr)
            _logger.LogError("Key exchange failed: {Message}", result.UnwrapErr().Message);
        else
            await RegisterDeviceAsync(connectId, appDevice, CancellationToken.None);
    }

    private async Task RegisterDeviceAsync(
        uint connectId,
        AppDevice appDevice,
        CancellationToken token)
    {
        await _networkController.ExecuteServiceAction(
            connectId, RcpServiceAction.RegisterAppDevice,
            appDevice.ToByteArray(), ServiceFlowType.Single,
            decryptedPayload =>
            {
                AppDeviceRegisteredStateReply reply =
                    Utilities.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);
                Guid appServerInstanceId = Utilities.FromByteStringToGuid(reply.UniqueId);
                AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;
                lock (_lock)
                {
                    appInstanceInfo.SystemDeviceIdentifier = appServerInstanceId;
                }

                _logger.LogInformation("Device registered with ID: {AppServerInstanceId}", appServerInstanceId);

                return Task.FromResult(Result<Unit, ShieldFailure>.Ok(Unit.Value));
            }, token);
    }
    
    
    
}