using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.Hosts;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Views.Core;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public class ApplicationRouter(
    IClassicDesktopStyleApplicationLifetime desktop,
    IModuleManager moduleManager,
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider) : IApplicationRouter
{
    private const int FadeDurationMs = 500;
    private const int WindowShowDelayMs = 50;
    private const int FrameDelayMs = 16;

    public async Task NavigateToAuthenticationAsync()
    {
        Log.Information("[ROUTER-NAV] Starting navigation to Authentication window");

        Window? currentWindow = desktop.MainWindow;
        if (currentWindow == null)
        {
            Log.Warning("[ROUTER-NAV] Unable to navigate to authentication: current window is null");
            throw new InvalidOperationException("Cannot navigate: current window is null");
        }

        string currentWindowType = currentWindow.GetType().Name;
        bool isCurrentWindowVisible = await Dispatcher.UIThread.InvokeAsync(() => currentWindow.IsVisible);
        Log.Information("[ROUTER-NAV] Current window type: {Type}, IsVisible: {IsVisible}",
            currentWindowType, isCurrentWindowVisible);

        IModule authModule = await moduleManager.LoadModuleAsync("Authentication").ConfigureAwait(false);

        if (authModule.ServiceScope?.ServiceProvider == null)
        {
            Log.Error("[ROUTER-NAV] Failed to load Authentication module");
            throw new InvalidOperationException("Failed to load Authentication module");
        }

        MembershipHostWindowModel? membershipViewModel =
            authModule.ServiceScope.ServiceProvider.GetService<MembershipHostWindowModel>();

        if (membershipViewModel == null)
        {
            Log.Error("[ROUTER-NAV] Failed to create MembershipHostWindowModel");
            throw new InvalidOperationException("Failed to create MembershipHostWindowModel");
        }

        MembershipHostWindow authWindow = await Dispatcher.UIThread.InvokeAsync(() => new MembershipHostWindow
        {
            DataContext = membershipViewModel
        });

        Log.Information("[ROUTER-NAV] Authentication window created, preparing transition");
        await PrepareAndShowWindowAsync(authWindow).ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Starting fade transition from {From} to {To}",
            currentWindow.GetType().Name, authWindow.GetType().Name);
        await PerformFadeTransitionAsync(currentWindow, authWindow).ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Fade transition complete, unloading Main module");
        await moduleManager.UnloadModuleAsync("Main").ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Ensuring anonymous protocol is available");
        await EnsureAnonymousProtocolAsync().ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Navigation to Authentication completed successfully");
    }

    public async Task NavigateToMainAsync()
    {
        Log.Information("[ROUTER-NAV] Starting navigation to Main window");

        Window? currentWindow = desktop.MainWindow;
        if (currentWindow == null)
        {
            Log.Warning("[ROUTER-NAV] Unable to navigate to main: current window is null");
            throw new InvalidOperationException("Cannot navigate: current window is null");
        }

        string currentWindowType = currentWindow.GetType().Name;
        bool isCurrentWindowVisible = await Dispatcher.UIThread.InvokeAsync(() => currentWindow.IsVisible);
        Log.Information("[ROUTER-NAV] Current window type: {Type}, IsVisible: {IsVisible}",
            currentWindowType, isCurrentWindowVisible);

        IModule mainModule = await moduleManager.LoadModuleAsync("Main").ConfigureAwait(false);

        if (mainModule.ServiceScope?.ServiceProvider == null)
        {
            Log.Error("[ROUTER-NAV] Failed to load Main module");
            throw new InvalidOperationException("Failed to load Main module");
        }

        MainViewModel? mainViewModel =
            mainModule.ServiceScope.ServiceProvider.GetService<MainViewModel>();

        if (mainViewModel == null)
        {
            Log.Error("[ROUTER-NAV] Failed to create MainViewModel");
            throw new InvalidOperationException("Failed to create MainViewModel");
        }

        MainHostWindow mainWindow = await Dispatcher.UIThread.InvokeAsync(() => new MainHostWindow
        {
            DataContext = mainViewModel
        });

        Log.Information("[ROUTER-NAV] Main window created, preparing transition");
        await PrepareAndShowWindowAsync(mainWindow).ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Starting fade transition from {From} to {To}",
            currentWindow.GetType().Name, mainWindow.GetType().Name);
        await PerformFadeTransitionAsync(currentWindow, mainWindow).ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Fade transition complete, unloading Authentication module");
        await moduleManager.UnloadModuleAsync("Authentication").ConfigureAwait(false);

        Log.Information("[ROUTER-NAV] Navigation to Main completed successfully");
    }

    public async Task TransitionFromSplashAsync(Window splashWindow, bool isAuthenticated)
    {
        Log.Information("[ROUTER] TransitionFromSplash called. IsAuthenticated: {IsAuthenticated}", isAuthenticated);
        Window nextWindow;

        if (isAuthenticated)
        {
            Log.Information("[ROUTER] Loading Main module");
            IModule mainModule = await moduleManager.LoadModuleAsync("Main").ConfigureAwait(false);

            if (mainModule.ServiceScope?.ServiceProvider == null)
            {
                Log.Error("[ROUTER] Failed to load Main module from splash");
                throw new InvalidOperationException("Failed to load Main module from splash");
            }

            MainViewModel? mainViewModel =
                mainModule.ServiceScope.ServiceProvider.GetService<MainViewModel>();

            nextWindow = await Dispatcher.UIThread.InvokeAsync(() => new MainHostWindow
            {
                DataContext = mainViewModel
            });
            Log.Information("[ROUTER] Main window created");
        }
        else
        {
            Log.Information("[ROUTER] Loading Authentication module");
            IModule authModule = await moduleManager.LoadModuleAsync("Authentication").ConfigureAwait(false);
            Log.Information("[ROUTER] Authentication module loaded. ServiceScope null: {IsNull}", authModule.ServiceScope == null);

            if (authModule.ServiceScope?.ServiceProvider == null)
            {
                Log.Error("[ROUTER] Failed to load Authentication module from splash");
                throw new InvalidOperationException("Failed to load Authentication module from splash");
            }

            Log.Information("[ROUTER] Getting MembershipHostWindowModel from service provider");
            MembershipHostWindowModel? membershipViewModel =
                authModule.ServiceScope.ServiceProvider.GetService<MembershipHostWindowModel>();
            Log.Information("[ROUTER] MembershipHostWindowModel retrieved. Null: {IsNull}", membershipViewModel == null);

            Log.Information("[ROUTER] Creating MembershipHostWindow");
            nextWindow = await Dispatcher.UIThread.InvokeAsync(() => new MembershipHostWindow
            {
                DataContext = membershipViewModel
            });
            Log.Information("[ROUTER] Authentication window created");
        }

        Log.Information("[ROUTER] Preparing and showing next window");
        await PrepareAndShowWindowAsync(nextWindow).ConfigureAwait(false);
        Log.Information("[ROUTER] Setting as main window");
        desktop.MainWindow = nextWindow;
        Log.Information("[ROUTER] Starting fade transition");
        await PerformFadeTransitionAsync(splashWindow, nextWindow).ConfigureAwait(false);
        Log.Information("[ROUTER] Transition complete");
    }

    private async Task PrepareAndShowWindowAsync(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Opacity = 0;
            window.Show();
        });
        await Task.Delay(WindowShowDelayMs).ConfigureAwait(false);
    }

    private async Task PerformFadeTransitionAsync(Window fromWindow, Window toWindow)
    {
        Log.Information("[ROUTER-FADE] Starting fade transition from {From} to {To}",
            fromWindow.GetType().Name, toWindow.GetType().Name);

        TimeSpan duration = TimeSpan.FromMilliseconds(FadeDurationMs);
        DateTime start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < duration)
        {
            double progress = (DateTime.UtcNow - start).TotalMilliseconds / FadeDurationMs;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                fromWindow.Opacity = 1 - progress;
                toWindow.Opacity = progress;
            });
            await Task.Delay(FrameDelayMs).ConfigureAwait(false);
        }

        Log.Information("[ROUTER-FADE] Fade animation complete, starting window close sequence");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                Log.Information("[ROUTER-CLOSE] Attempting to close old window. Type: {Type}, IsVisible: {IsVisible}",
                    fromWindow.GetType().Name, fromWindow.IsVisible);

                fromWindow.Opacity = 0;
                toWindow.Opacity = 1;

                Window? oldMainWindow = desktop.MainWindow;
                Log.Information("[ROUTER-CLOSE] Current desktop.MainWindow: {Type}",
                    oldMainWindow?.GetType().Name ?? "null");

                desktop.MainWindow = toWindow;
                Log.Information("[ROUTER-CLOSE] desktop.MainWindow set to new window: {Type}",
                    toWindow.GetType().Name);

                Log.Information("[ROUTER-CLOSE] Hiding old window for immediate visual feedback");
                fromWindow.Hide();

                Log.Information("[ROUTER-CLOSE] Calling fromWindow.Close()...");
                fromWindow.Close();

                Log.Information("[ROUTER-CLOSE] fromWindow.Close() completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ROUTER-CLOSE-ERROR] Failed to close old window. Type: {Type}, Message: {Message}",
                    fromWindow.GetType().Name, ex.Message);

                try
                {
                    Log.Warning("[ROUTER-CLOSE-ERROR] Attempting force hide as fallback");
                    fromWindow.Hide();
                    fromWindow.Opacity = 0;
                    Log.Information("[ROUTER-CLOSE-ERROR] Force hide succeeded");
                }
                catch (Exception hideEx)
                {
                    Log.Error(hideEx, "[ROUTER-CLOSE-ERROR] Even Hide() failed: {Message}", hideEx.Message);
                }
            }
        });

        await Task.Delay(100).ConfigureAwait(false);

        Log.Information("[ROUTER-CLOSE-VERIFY] Verifying old window closed successfully");
        bool isStillVisible = await Dispatcher.UIThread.InvokeAsync(() => fromWindow.IsVisible);

        if (isStillVisible)
        {
            Log.Warning("[ROUTER-CLOSE-VERIFY] Window still visible after close! Attempting second close...");
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    fromWindow.Hide();
                    fromWindow.Close();
                });
                Log.Information("[ROUTER-CLOSE-VERIFY] Second close attempt completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ROUTER-CLOSE-VERIFY] Second close attempt failed: {Message}", ex.Message);
            }
        }
        else
        {
            Log.Information("[ROUTER-CLOSE-VERIFY] Window successfully closed ✓");
        }

        Log.Information("[ROUTER-FADE] Transition complete");
    }

    private async Task EnsureAnonymousProtocolAsync()
    {
        try
        {
            Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
                await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

            if (settingsResult.IsErr)
            {
                Log.Warning("[ROUTER-PROTOCOL] Failed to get application settings: {Error}",
                    settingsResult.UnwrapErr().Message);
                return;
            }

            ApplicationInstanceSettings settings = settingsResult.Unwrap();

            if (settings.Membership != null)
            {
                Log.Information("[ROUTER-PROTOCOL] User has membership, skipping anonymous protocol creation");
                return;
            }

            uint connectId = NetworkProvider.ComputeUniqueConnectId(settings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

            if (networkProvider.HasConnection(connectId))
            {
                Log.Information("[ROUTER-PROTOCOL] Anonymous protocol already exists. ConnectId: {ConnectId}",
                    connectId);
                return;
            }

            Log.Information("[ROUTER-PROTOCOL] Creating anonymous protocol. ConnectId: {ConnectId}", connectId);
            networkProvider.InitiateEcliptixProtocolSystem(settings, connectId);

            Log.Information("[ROUTER-PROTOCOL] Establishing anonymous protocol handshake. ConnectId: {ConnectId}",
                connectId);
            Result<EcliptixSessionState, NetworkFailure> establishResult =
                await networkProvider.EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

            if (establishResult.IsErr)
            {
                Log.Error("[ROUTER-PROTOCOL] Failed to establish anonymous protocol: {Error}",
                    establishResult.UnwrapErr().Message);
                return;
            }

            Log.Information(
                "[ROUTER-PROTOCOL] Anonymous protocol created and handshake completed successfully. ConnectId: {ConnectId}",
                connectId);

            Log.Information("[ROUTER-PROTOCOL] Calling RegisterDevice to fetch server public key. ConnectId: {ConnectId}",
                connectId);

            Result<Unit, NetworkFailure> registerResult = await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);

            if (registerResult.IsErr)
            {
                Log.Error("[ROUTER-PROTOCOL] RegisterDevice failed. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, registerResult.UnwrapErr().Message);
                return;
            }

            Log.Information("[ROUTER-PROTOCOL] RegisterDevice completed successfully. ConnectId: {ConnectId}",
                connectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ROUTER-PROTOCOL] Failed to ensure anonymous protocol");
        }
    }

    private async Task<Result<Unit, NetworkFailure>> RegisterDeviceAsync(uint connectId,
        ApplicationInstanceSettings settings)
    {
        AppDevice appDevice = new()
        {
            AppInstanceId = settings.AppInstanceId,
            DeviceId = settings.DeviceId,
            DeviceType = AppDevice.Types.DeviceType.Desktop
        };

        return await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegisterAppDevice,
            SecureByteStringInterop.WithByteStringAsSpan(appDevice.ToByteString(),
                span => span.ToArray()),
            decryptedPayload =>
            {
                AppDeviceRegisteredStateReply reply =
                    Helpers.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);
                Guid appServerInstanceId = Helpers.FromByteStringToGuid(reply.UniqueId);

                settings.SystemDeviceIdentifier = appServerInstanceId.ToString();
                settings.ServerPublicKey = SecureByteStringInterop.WithByteStringAsSpan(reply.ServerPublicKey,
                    ByteString.CopyFrom);

                Log.Information("[ROUTER-PROTOCOL-REGISTER] Server public key updated. AppServerInstanceId: {InstanceId}",
                    appServerInstanceId);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None).ConfigureAwait(false);
    }
}
