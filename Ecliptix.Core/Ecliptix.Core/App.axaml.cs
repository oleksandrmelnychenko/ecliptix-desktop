using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Network;
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
using Splat;

namespace Ecliptix.Core;

public class App : Application
{
    private const int DefaultOneTimeKeyCount = 10;

    private const string AppDeviceSecrecyChannelStateKey = "Ecliptix.SessionState.v1";

    private readonly Lock _lock = new();

    private ILogger<App> Logger => Locator.Current.GetService<ILogger<App>>()!;
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
            = SetApplicationInstanceSettings();

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

    private Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>
        SetApplicationInstanceSettings()
    {
        const string settingsKey = "ApplicationInstanceSettings";

        Result<Option<byte[]>, InternalServiceApiFailure> applicationInstanceSettingsResult =
            SecureStorageProvider.TryGetByKey(settingsKey);

        if (applicationInstanceSettingsResult.IsErr)
            return Result<(ApplicationInstanceSettings, bool), InternalServiceApiFailure>.Err(
                applicationInstanceSettingsResult.UnwrapErr());

        if (!applicationInstanceSettingsResult.Unwrap().HasValue)
        {
            ApplicationInstanceSettings applicationInstanceSettings = new()
            {
                AppInstanceId = Utilities.GuidToByteString(Guid.NewGuid()),
                DeviceId = Utilities.GuidToByteString(Guid.NewGuid())
            };
            SecureStorageProvider.Store(settingsKey, applicationInstanceSettings.ToByteArray());
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
            NetworkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings,
                DefaultOneTimeKeyCount, connectId);

            await NetworkProvider.EstablishSecrecyChannel(connectId,
                state => { SecureStorageProvider.Store(connectId.ToString(), state.ToByteArray()); },
                failure => { ShutdownApplication("Failed to establish secrecy channel."); });

            AppDevice appDevice = new()
            {
                AppInstanceId = applicationInstanceSettings.AppInstanceId,
                DeviceId = applicationInstanceSettings.DeviceId,
                DeviceType = AppDevice.Types.DeviceType.Desktop
            };

            await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings, CancellationToken.None);
        }
        else
        {
            //SecureStorageProvider.Delete(connectId.ToString());
            Result<Option<byte[]>, InternalServiceApiFailure> ecliptixSecrecyChannelStateResult =
                SecureStorageProvider.TryGetByKey(connectId.ToString());

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
                    Logger.LogError("Failed to restore secrecy channel: {Error}",
                        restoreSecrecyChannelResult.UnwrapErr().Message);
                    ShutdownApplication("Failed to restore secrecy channel.");
                    return;
                }

                bool isSynchronized = restoreSecrecyChannelResult.Unwrap();
                if (!isSynchronized)
                {
                    await NetworkProvider.EstablishSecrecyChannel(connectId,
                        state => { SecureStorageProvider.Store(connectId.ToString(), state.ToByteArray()); },
                        failure =>
                        {
                            ShutdownApplication("Failed to establish secrecy channel after failed restoration.");
                        });

                    AppDevice appDevice = new()
                    {
                        AppInstanceId = applicationInstanceSettings.AppInstanceId,
                        DeviceId = applicationInstanceSettings.DeviceId,
                        DeviceType = AppDevice.Types.DeviceType.Desktop
                    };

                    await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings,
                        CancellationToken.None);
                }
                else
                {
                    AppDevice appDevice = new()
                    {
                        AppInstanceId = applicationInstanceSettings.AppInstanceId,
                        DeviceId = applicationInstanceSettings.DeviceId,
                        DeviceType = AppDevice.Types.DeviceType.Desktop
                    };

                    await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings,
                        CancellationToken.None);
                }
            }
            else
            {
                NetworkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, DefaultOneTimeKeyCount,
                    connectId);

                await NetworkProvider.EstablishSecrecyChannel(connectId,
                    state => { SecureStorageProvider.Store(connectId.ToString(), state.ToByteArray()); },
                    failure => { ShutdownApplication("Failed to establish secrecy channel."); });

                AppDevice appDevice = new()
                {
                    AppInstanceId = applicationInstanceSettings.AppInstanceId,
                    DeviceId = applicationInstanceSettings.DeviceId,
                    DeviceType = AppDevice.Types.DeviceType.Desktop
                };

                await RegisterDeviceAsync(connectId, appDevice, applicationInstanceSettings, CancellationToken.None);
            }
        }
    }

    private async Task RegisterDeviceAsync(
        uint connectId,
        AppDevice appDevice,
        ApplicationInstanceSettings applicationInstanceSettings,
        CancellationToken token)
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

                lock (_lock)
                {
                    applicationInstanceSettings.SystemDeviceIdentifier = appServerInstanceId.ToString();
                    applicationInstanceSettings.ServerPublicKey =
                        ByteString.CopyFrom(reply.ServerPublicKey.ToByteArray());
                }

                Logger.LogInformation("Device successfully registered with server ID: {AppServerInstanceId}",
                    appServerInstanceId);
                return Task.FromResult(Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
            }, token);

        if (result.IsErr)
        {
            Logger.LogError("Device registration failed: {Error}", result.UnwrapErr().Message);
            ShutdownApplication("Device registration failed.");
        }
    }

    private void ShutdownApplication(string reason)
    {
        Logger.LogError("Shutting down application: {Reason}", reason);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // Handle single view platform shutdown if necessary
        }
    }
}