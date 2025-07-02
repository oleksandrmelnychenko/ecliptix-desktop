using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Authentication;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class App : Application
{
    private NetworkProvider NetworkProvider => Locator.Current.GetService<NetworkProvider>()!;
    private ISecureStorageProvider SecureStorageProvider => Locator.Current.GetService<ISecureStorageProvider>()!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure> applicationInstanceSettingsResult
            = SetApplicationInstanceSettings().GetAwaiter().GetResult();

        if (applicationInstanceSettingsResult.IsErr)
        {
            ShutdownApplication("Failed to set application instance settings.");
            return;
        }

        (ApplicationInstanceSettings applicationInstanceSettings, bool isNewInstance) =
            applicationInstanceSettingsResult.Unwrap();

        _ = InitializeApplication(applicationInstanceSettings, isNewInstance);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new AuthenticationWindow
            {
                DataContext = Locator.Current.GetService<AuthenticationViewModel>()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = Locator.Current.GetService<MainViewModel>()
            };
        }
    }

    private async Task<Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>>
        SetApplicationInstanceSettings()
    {
        const string settingsKey = "ApplicationInstanceSettings";

        Result<Option<byte[]>, InternalServiceApiFailure> applicationInstanceSettingsResult =
            await SecureStorageProvider.TryGetByKeyAsync(settingsKey);

        if (applicationInstanceSettingsResult.IsErr)
        {
            return Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>.Err(
                applicationInstanceSettingsResult.UnwrapErr());
        }

        if (!applicationInstanceSettingsResult.Unwrap().HasValue)
        {
            ApplicationInstanceSettings applicationInstanceSettings = new()
            {
                AppInstanceId = Utilities.GuidToByteString(Guid.NewGuid()),
                DeviceId = Utilities.GuidToByteString(Guid.NewGuid())
            };
            await SecureStorageProvider.StoreAsync(settingsKey, applicationInstanceSettings.ToByteArray());
            return Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>.Ok(
                (applicationInstanceSettings, true));
        }

        byte[] existingSettings = applicationInstanceSettingsResult.Unwrap().Value!;
        return Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>.Ok(
            (ApplicationInstanceSettings.Parser.ParseFrom(existingSettings),
                false));
    }

    private async Task InitializeApplication(ApplicationInstanceSettings applicationInstanceSettings,
        bool isNewInstance)
    {
        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            applicationInstanceSettings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        if (isNewInstance)
        {
            NetworkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);

            Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> establishSecrecyChannelResult =
                await NetworkProvider.EstablishSecrecyChannel(connectId);

            if (establishSecrecyChannelResult.IsErr)
            {
            }

            EcliptixSecrecyChannelState ecliptixSecrecyChannelState = establishSecrecyChannelResult.Unwrap();

            await SecureStorageProvider.StoreAsync(connectId.ToString(), ecliptixSecrecyChannelState.ToByteArray());
            await RegisterAppDevice(applicationInstanceSettings, connectId);
        }
        else
        {
            Result<Option<byte[]>, InternalServiceApiFailure> ecliptixSecrecyChannelStateResult =
                await SecureStorageProvider.TryGetByKeyAsync(connectId.ToString());

            if (ecliptixSecrecyChannelStateResult.IsErr)
            {
                ShutdownApplication("Failed to retrieve secrecy channel state.");
                return;
            }

            Option<byte[]> maybeState = ecliptixSecrecyChannelStateResult.Unwrap();
            if (maybeState.HasValue)
            {
                EcliptixSecrecyChannelState ecliptixSecrecyChannelState =
                    EcliptixSecrecyChannelState.Parser.ParseFrom(maybeState.Value);

                Result<bool, EcliptixProtocolFailure> restoreSecrecyChannelResult =
                    await NetworkProvider.RestoreSecrecyChannel(ecliptixSecrecyChannelState,
                        applicationInstanceSettings);

                if (restoreSecrecyChannelResult.IsErr)
                {
                    Log.Error("Failed to restore secrecy channel: {Error}",
                        restoreSecrecyChannelResult.UnwrapErr().Message);
                    ShutdownApplication("Failed to restore secrecy channel.");
                    return;
                }

                bool isSynchronized = restoreSecrecyChannelResult.Unwrap();
                if (!isSynchronized)
                {
                    await NetworkProvider.EstablishSecrecyChannel(connectId);

                    AppDevice appDevice = new()
                    {
                        AppInstanceId = applicationInstanceSettings.AppInstanceId,
                        DeviceId = applicationInstanceSettings.DeviceId,
                        DeviceType = AppDevice.Types.DeviceType.Desktop
                    };

                    await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings);
                }
                else
                {
                    AppDevice appDevice = new()
                    {
                        AppInstanceId = applicationInstanceSettings.AppInstanceId,
                        DeviceId = applicationInstanceSettings.DeviceId,
                        DeviceType = AppDevice.Types.DeviceType.Desktop
                    };

                    await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings);
                }
            }
            else
            {
                NetworkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings,
                    connectId);

                Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> secrecyChannelStateResult =
                    await NetworkProvider.EstablishSecrecyChannel(connectId);

                if (secrecyChannelStateResult.IsErr)
                { 
                    Log.Error("Failed to establish secrecy channel: {Error}",
                        secrecyChannelStateResult.UnwrapErr().Message);
                    ShutdownApplication("Failed to establish secrecy channel.");
                    return;
                }

                EcliptixSecrecyChannelState ecliptixSecrecyChannelState = secrecyChannelStateResult.Unwrap();

                await SecureStorageProvider.StoreAsync(connectId.ToString(), ecliptixSecrecyChannelState.ToByteArray());

                AppDevice appDevice = new()
                {
                    AppInstanceId = applicationInstanceSettings.AppInstanceId,
                    DeviceId = applicationInstanceSettings.DeviceId,
                    DeviceType = AppDevice.Types.DeviceType.Desktop
                };

                await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings);
            }
        }
    }

    private async Task RegisterAppDevice(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        AppDevice appDevice = new()
        {
            AppInstanceId = applicationInstanceSettings.AppInstanceId,
            DeviceId = applicationInstanceSettings.DeviceId,
            DeviceType = AppDevice.Types.DeviceType.Desktop
        };

        await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings);
    }

    private async Task RegisterDeviceAsync(
        uint connectId,
        AppDevice appDevice,
        ApplicationInstanceSettings applicationInstanceSettings
    )
    {
        Result<Unit, EcliptixProtocolFailure> result = await NetworkProvider.ExecuteServiceRequest(
            connectId,
            RcpServiceType.RegisterAppDevice,
            appDevice.ToByteArray(),
            ServiceFlowType.Single,
            decryptedPayload =>
            {
                AppDeviceRegisteredStateReply reply =
                    Utilities.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);
                Guid appServerInstanceId = Utilities.FromByteStringToGuid(reply.UniqueId);

                applicationInstanceSettings.SystemDeviceIdentifier = appServerInstanceId.ToString();
                applicationInstanceSettings.ServerPublicKey =
                    ByteString.CopyFrom(reply.ServerPublicKey.ToByteArray());

                Log.Information("Device successfully registered with server ID: {AppServerInstanceId}",
                    appServerInstanceId);
                return Task.FromResult(Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
            }, CancellationToken.None);

        if (result.IsErr)
        {
            Log.Error("Device registration failed: {Error}", result.UnwrapErr().Message);
            ShutdownApplication("Device registration failed.");
        }
    }

    private void ShutdownApplication(string reason)
    {
        Log.Error("Shutting down application: {Reason}", reason);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            
        }
    }
}