using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Network;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Memberships;
using Splat;

namespace Ecliptix.Core;

public partial class App : Application
{
    public App()
    {
        /*
        EcliptixSystemIdentityKeys aliceKeys = EcliptixSystemIdentityKeys.Create(5).Unwrap();
        EcliptixProtocolSystem ecliptixProtocolSystem = new(aliceKeys);

        PubKeyExchange keyExchange =
            ecliptixProtocolSystem.BeginDataCenterPubKeyExchange(1, PubKeyExchangeType
                .AppDeviceEphemeralConnect);

        Task<PubKeyExchange> establishEphemeralConnectAsync =
            _appDeviceServiceHandler.EstablishEphemeralConnectAsync(keyExchange);
        establishEphemeralConnectAsync.Wait();

        PubKeyExchange peerPubKeys =
            establishEphemeralConnectAsync.Result;

        ecliptixProtocolSystem.CompleteDataCenterPubKeyExchange(1,
            PubKeyExchangeType.AppDeviceEphemeralConnect, peerPubKeys);

        byte[] appDevice = new AppDevice
        {
            DeviceId = ByteString.CopyFrom(applicationController.DeviceId.ToByteArray()),
            DeviceType = AppDevice.Types.DeviceType.Desktop,
            AppInstanceId = ByteString.CopyFrom(applicationController.AppInstanceId.ToByteArray()),
        }.ToByteArray();

        CipherPayload payload = ecliptixProtocolSystem.ProduceOutboundMessage(
            1, PubKeyExchangeType.AppDeviceEphemeralConnect, appDevice
        );

        Task<CipherPayload> registeredDeviceAppTask = appDeviceServiceHandler.RegisterDeviceAppIfNotExistAsync(payload);
        registeredDeviceAppTask.Wait();

        CipherPayload regResp = registeredDeviceAppTask.Result;

        byte[] x = ecliptixProtocolSystem.ProcessInboundMessage(1,
            PubKeyExchangeType.AppDeviceEphemeralConnect, regResp);

        AppDeviceRegisteredStateReply appDeviceRegisteredStateReply = ServiceUtilities.ParseFromBytes<AppDeviceRegisteredStateReply>(x);
        Guid systemAppDevice = ServiceUtilities.FromByteStringToGuid(appDeviceRegisteredStateReply.UniqueId);

        applicationController.SystemAppDeviceId = systemAppDevice;*/
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppSettings? appSettings = Locator.Current.GetService<AppSettings>();
        if (appSettings == null)
        {
            //TODO: load store to get the settings.
        }

        const bool isAuthorized = false;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AuthenticationViewModel authViewModel = Locator.Current.GetService<AuthenticationViewModel>()!;
            desktop.MainWindow = new AuthenticationWindow
            {
                DataContext = authViewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = Locator.Current.GetService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}