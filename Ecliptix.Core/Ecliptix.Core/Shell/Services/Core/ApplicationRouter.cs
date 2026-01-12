using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Authentication;
using Ecliptix.Core.Modularity.Main;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.ViewModels;
using Ecliptix.Core.Shell.Views;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Shell.Services.Core;

public sealed class ApplicationRouter(
    IClassicDesktopStyleApplicationLifetime desktop,
    IModuleManager moduleManager,
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    MainWindowViewModel mainWindowViewModel) : IApplicationRouter
{
    private const int FADE_DURATION_MS = 500;
    private const int WINDOW_SHOW_DELAY_MS = 50;
    private const int FRAME_DELAY_MS = 16;
    private const int WINDOW_CLOSE_CHECK_DELAY_MS = 100;

    private static readonly string AuthModuleName = ModuleIdentifier.AUTHENTICATION.ToName();
    private static readonly string MainModuleName = ModuleIdentifier.MAIN.ToName();

    public async Task NavigateToAuthenticationAsync()
    {
        IModule authModule = await LoadModuleOrThrowAsync(
            ModuleIdentifier.AUTHENTICATION,
            ApplicationErrorMessages.ApplicationRouter.FAILED_TO_LOAD_AUTH_MODULE).ConfigureAwait(false);

        IAuthenticationHost membershipViewModel = GetRequiredServiceOrThrow<IAuthenticationHost>(
            authModule.ServiceScope!.ServiceProvider,
            ApplicationErrorMessages.ApplicationRouter.FAILED_TO_CREATE_MEMBERSHIP_VIEW_MODEL);

        await mainWindowViewModel.SetAuthenticationContentAsync(membershipViewModel).ConfigureAwait(false);
        await moduleManager.UnloadModuleAsync(MainModuleName).ConfigureAwait(false);
        await EnsureAnonymousProtocolAsync().ConfigureAwait(false);
    }

    public async Task NavigateToMainAsync()
    {
        IModule mainModule = await LoadModuleOrThrowAsync(
            ModuleIdentifier.MAIN,
            ApplicationErrorMessages.ApplicationRouter.FAILED_TO_LOAD_MAIN_MODULE).ConfigureAwait(false);

        IMainHost mainViewModel = GetRequiredServiceOrThrow<IMainHost>(
            mainModule.ServiceScope!.ServiceProvider,
            ApplicationErrorMessages.ApplicationRouter.FAILED_TO_CREATE_MAIN_VIEW_MODEL);

        await mainWindowViewModel.SetMainContentAsync(mainViewModel).ConfigureAwait(false);
        await moduleManager.UnloadModuleAsync(AuthModuleName).ConfigureAwait(false);
    }

    public async Task TransitionFromSplashAsync(Window splashWindow, bool isAuthenticated)
    {
        MainWindow mainWindow = await Dispatcher.UIThread.InvokeAsync(() => new MainWindow
        {
            DataContext = mainWindowViewModel
        });

        if (isAuthenticated)
        {
            IModule mainModule = await LoadModuleOrThrowAsync(
                ModuleIdentifier.MAIN,
                ApplicationErrorMessages.ApplicationRouter.FAILED_TO_LOAD_MAIN_MODULE_FROM_SPLASH).ConfigureAwait(false);

            IMainHost mainViewModel = GetRequiredServiceOrThrow<IMainHost>(
                mainModule.ServiceScope!.ServiceProvider,
                ApplicationErrorMessages.ApplicationRouter.FAILED_TO_CREATE_MAIN_VIEW_MODEL);

            await mainWindowViewModel.SetMainContentAsync(mainViewModel).ConfigureAwait(false);
        }
        else
        {
            IModule authModule = await LoadModuleOrThrowAsync(
                ModuleIdentifier.AUTHENTICATION,
                ApplicationErrorMessages.ApplicationRouter.FAILED_TO_LOAD_AUTH_MODULE_FROM_SPLASH).ConfigureAwait(false);

            IAuthenticationHost membershipViewModel = GetRequiredServiceOrThrow<IAuthenticationHost>(
                authModule.ServiceScope!.ServiceProvider,
                ApplicationErrorMessages.ApplicationRouter.FAILED_TO_CREATE_MEMBERSHIP_VIEW_MODEL);

            await mainWindowViewModel.SetAuthenticationContentAsync(membershipViewModel).ConfigureAwait(false);
        }

        await PrepareAndShowWindowAsync(mainWindow).ConfigureAwait(false);
        desktop.MainWindow = mainWindow;
        await PerformFadeTransitionAsync(splashWindow, mainWindow).ConfigureAwait(false);
    }

    private static async Task PrepareAndShowWindowAsync(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Opacity = 0;
            window.Show();
        });
        await Task.Delay(WINDOW_SHOW_DELAY_MS).ConfigureAwait(false);
    }

    private async Task PerformFadeTransitionAsync(Window fromWindow, Window toWindow)
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(FADE_DURATION_MS);
        DateTime start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < duration)
        {
            double progress = (DateTime.UtcNow - start).TotalMilliseconds / FADE_DURATION_MS;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                fromWindow.Opacity = 1 - progress;
                toWindow.Opacity = progress;
            });
            await Task.Delay(FRAME_DELAY_MS).ConfigureAwait(false);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                fromWindow.Opacity = 0;
                toWindow.Opacity = 1;

                desktop.MainWindow = toWindow;
                fromWindow.Hide();
                fromWindow.Close();
            }
            catch
            {
                fromWindow.Hide();
                fromWindow.Opacity = 0;
            }
        });

        await Task.Delay(WINDOW_CLOSE_CHECK_DELAY_MS).ConfigureAwait(false);
        bool isStillVisible = await Dispatcher.UIThread.InvokeAsync(() => fromWindow.IsVisible);

        if (isStillVisible)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                fromWindow.Hide();
                fromWindow.Close();
            });
        }
    }

    private async Task EnsureAnonymousProtocolAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return;
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        if (settings.Membership != null)
        {
            return;
        }

        uint connectId = NetworkProvider.ComputeUniqueConnectId(settings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        if (networkProvider.HasConnection(connectId))
        {
            return;
        }

        networkProvider.InitiateEcliptixProtocolSystem(settings, connectId);

        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
        {
            return;
        }

        await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);
    }

    private async Task RegisterDeviceAsync(uint connectId,
        ApplicationInstanceSettings settings)
    {
        Device device = new()
        {
            ApplicationInstanceId = settings.AppInstanceId,
            DeviceId = settings.DeviceId,
            DeviceType = DeviceType.Desktop
        };

        await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegisterAppDevice,
            SecureByteStringInterop.WithByteStringAsSpan(device.ToByteString(),
                span => span.ToArray()),
            decryptedPayload =>
            {
                DeviceRegistrationResponse reply =
                    Helpers.ParseFromBytes<DeviceRegistrationResponse>(decryptedPayload);

                if (reply.Result is DeviceRegistrationResponse.Types.Result.DeviceRegistrationResultInvalidRequest
                    or DeviceRegistrationResponse.Types.Result.DeviceRegistrationResultInternalError)
                {
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType(
                            string.IsNullOrWhiteSpace(reply.Message)
                                ? "Device registration failed"
                                : reply.Message)));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: false, token: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<IModule> LoadModuleOrThrowAsync(ModuleIdentifier id, string failureMessage)
    {
        Option<IModule> moduleOption = await moduleManager.LoadModuleAsync(id.ToName()).ConfigureAwait(false);

        if (!moduleOption.IsSome || moduleOption.Value!.ServiceScope?.ServiceProvider == null)
        {
            throw new InvalidOperationException(failureMessage);
        }

        return moduleOption.Value!;
    }

    private static T GetRequiredServiceOrThrow<T>(IServiceProvider sp, string failureMessage) where T : class
    {
        T? service = sp.GetService<T>();
        if (service == null)
        {
            throw new InvalidOperationException(failureMessage);
        }

        return service;
    }
}
