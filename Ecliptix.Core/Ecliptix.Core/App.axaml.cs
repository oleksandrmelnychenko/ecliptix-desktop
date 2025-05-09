using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core;

public record KeyExchangeCompletedEvent;

public partial class App : Application
{
    private readonly CompositeDisposable _disposables = new();

    private readonly NetworkController _networkController;

    private const int DefaultOneTimeKeyCount = 10;

    public App()
    {
        _networkController = Locator.Current.GetService<NetworkController>()!;

        (uint connectId, AppDevice appDevice) = CreateEcliptixConnectionContext();

        MessageBus.Current.Listen<KeyExchangeCompletedEvent>()
            .Subscribe(_ =>
            {
                Task.Run(async () =>
                {
                    await _networkController.ExecuteServiceAction(
                        connectId, RcpServiceAction.RegisterAppDeviceIfNotExist,
                        appDevice.ToByteArray(), ServiceFlowType.Single,
                        async decryptedPayload =>
                        {
                            AppDeviceRegisteredStateReply reply =
                                Utilities.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);

                            Guid appServerInstanceId = Utilities.FromByteStringToGuid(reply.UniqueId);

                            return Result<Unit, ShieldFailure>.Ok(Unit.Value);
                        });
                });
            })
            .DisposeWith(_disposables);

        Task.Run(async () =>
        {
            Result<Unit, ShieldFailure> result =
                await _networkController.DataCenterPubKeyExchange(connectId);
            if (result.IsErr)
            {
                Console.WriteLine($"Key exchange failed: {result.UnwrapErr().Message}");
            }
        }).Wait();
    }

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