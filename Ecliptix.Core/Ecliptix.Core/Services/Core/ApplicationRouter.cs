using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.ViewModels.Core;
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

public sealed class ApplicationRouter(
    IClassicDesktopStyleApplicationLifetime desktop,
    IModuleManager moduleManager,
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    MainWindowViewModel mainWindowViewModel) : IApplicationRouter
{
    private const int FadeDurationMs = 500;
    private const int WindowShowDelayMs = 50;
    private const int FrameDelayMs = 16;

    private MainWindow? _mainWindow;

    public async Task NavigateToAuthenticationAsync()
    {
        IModule authModule = await moduleManager.LoadModuleAsync("Authentication").ConfigureAwait(false);

        if (authModule.ServiceScope?.ServiceProvider == null)
        {
            throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToLoadAuthModule);
        }

        AuthenticationViewModel? membershipViewModel =
            authModule.ServiceScope.ServiceProvider.GetService<AuthenticationViewModel>();

        if (membershipViewModel == null)
        {
            Log.Error("[ROUTER-NAV] Failed to create AuthenticationViewModel");
            throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToCreateMembershipViewModel);
        }

        Log.Debug("[ROUTER-NAV] Setting authentication content in MainWindow");
        await mainWindowViewModel.SetAuthenticationContentAsync(membershipViewModel).ConfigureAwait(false);

        Log.Debug("[ROUTER-NAV] Unloading Main module");
        await moduleManager.UnloadModuleAsync("Main").ConfigureAwait(false);

        Log.Debug("[ROUTER-NAV] Ensuring anonymous protocol is available");
        await EnsureAnonymousProtocolAsync().ConfigureAwait(false);

        Log.Debug("[ROUTER-NAV] Navigation to Authentication completed successfully");
    }

    public async Task NavigateToMainAsync()
    {
        Log.Debug("[ROUTER-NAV] Starting navigation to Main content");

        IModule mainModule = await moduleManager.LoadModuleAsync("Main").ConfigureAwait(false);

        if (mainModule.ServiceScope?.ServiceProvider == null)
        {
            Log.Error("[ROUTER-NAV] Failed to load Main module");
            throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToLoadMainModule);
        }

        MasterViewModel? mainViewModel =
            mainModule.ServiceScope.ServiceProvider.GetService<MasterViewModel>();

        if (mainViewModel == null)
        {
            Log.Error("[ROUTER-NAV] Failed to create MasterViewModel");
            throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToCreateMainViewModel);
        }

        Log.Debug("[ROUTER-NAV] Setting main content in MainWindow");
        await mainWindowViewModel.SetMainContentAsync(mainViewModel).ConfigureAwait(false);

        Log.Debug("[ROUTER-NAV] Unloading Authentication module");
        await moduleManager.UnloadModuleAsync("Authentication").ConfigureAwait(false);

        Log.Debug("[ROUTER-NAV] Navigation to Main completed successfully");
    }

    public async Task TransitionFromSplashAsync(Window splashWindow, bool isAuthenticated)
    {
        Log.Debug("[ROUTER] TransitionFromSplash called. IsAuthenticated: {IsAuthenticated}", isAuthenticated);

        Log.Debug("[ROUTER] Creating MainWindow (single window instance)");
        _mainWindow = await Dispatcher.UIThread.InvokeAsync(() => new MainWindow
        {
            DataContext = mainWindowViewModel
        });

        if (isAuthenticated)
        {
            Log.Debug("[ROUTER] Loading Main module");
            IModule mainModule = await moduleManager.LoadModuleAsync("Main").ConfigureAwait(false);

            if (mainModule.ServiceScope?.ServiceProvider == null)
            {
                Log.Error("[ROUTER] Failed to load Main module from splash");
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToLoadMainModuleFromSplash);
            }

            MasterViewModel? mainViewModel =
                mainModule.ServiceScope.ServiceProvider.GetService<MasterViewModel>();

            if (mainViewModel == null)
            {
                Log.Error("[ROUTER] Failed to create MasterViewModel");
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToCreateMainViewModel);
            }

            Log.Debug("[ROUTER] Setting Main content in MainWindow");
            await mainWindowViewModel.SetMainContentAsync(mainViewModel).ConfigureAwait(false);
        }
        else
        {
            Log.Debug("[ROUTER] Loading Authentication module");
            IModule authModule = await moduleManager.LoadModuleAsync("Authentication").ConfigureAwait(false);

            if (authModule.ServiceScope?.ServiceProvider == null)
            {
                Log.Error("[ROUTER] Failed to load Authentication module from splash");
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToLoadAuthModuleFromSplash);
            }

            AuthenticationViewModel? membershipViewModel =
                authModule.ServiceScope.ServiceProvider.GetService<AuthenticationViewModel>();

            if (membershipViewModel == null)
            {
                Log.Error("[ROUTER] Failed to create AuthenticationViewModel");
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter.FailedToCreateMembershipViewModel);
            }

            Log.Debug("[ROUTER] Setting Authentication content in MainWindow");
            await mainWindowViewModel.SetAuthenticationContentAsync(membershipViewModel).ConfigureAwait(false);
        }

        Log.Debug("[ROUTER] Preparing and showing MainWindow");
        await PrepareAndShowWindowAsync(_mainWindow).ConfigureAwait(false);
        Log.Debug("[ROUTER] Setting MainWindow as desktop.MainWindow");
        desktop.MainWindow = _mainWindow;
        Log.Debug("[ROUTER] Starting fade transition from splash to MainWindow");
        await PerformFadeTransitionAsync(splashWindow, _mainWindow).ConfigureAwait(false);
        Log.Debug("[ROUTER] Transition complete - MainWindow is now the single OS window");
    }

    private static async Task PrepareAndShowWindowAsync(Window window)
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
        Log.Debug("[ROUTER-FADE] Starting fade transition from {From} to {To}",
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

        Log.Debug("[ROUTER-FADE] Fade animation complete, starting window close sequence");

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
            catch (Exception ex)
            {
                Log.Error(ex, "[ROUTER-CLOSE-ERROR] Failed to close old window. Type: {Type}, Message: {Message}",
                    fromWindow.GetType().Name, ex.Message);

                try
                {
                    Log.Warning("[ROUTER-CLOSE-ERROR] Attempting force hide as fallback");
                    fromWindow.Hide();
                    fromWindow.Opacity = 0;
                    Log.Debug("[ROUTER-CLOSE-ERROR] Force hide succeeded");
                }
                catch (Exception hideEx)
                {
                    Log.Error(hideEx, "[ROUTER-CLOSE-ERROR] Even Hide() failed");
                }
            }
        });

        await Task.Delay(100).ConfigureAwait(false);

        Log.Debug("[ROUTER-CLOSE-VERIFY] Verifying old window closed successfully");
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
                Log.Debug("[ROUTER-CLOSE-VERIFY] Second close attempt completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ROUTER-CLOSE-VERIFY] Second close attempt failed");
            }
        }
        else
        {
            Log.Debug("[ROUTER-CLOSE-VERIFY] Window successfully closed ✓");
        }

        Log.Debug("[ROUTER-FADE] Transition complete");
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
                Log.Error("[ROUTER-PROTOCOL] Failed to establish anonymous protocol: {Error}",
                    establishResult.UnwrapErr().Message);
                return;
            }

            Result<Unit, NetworkFailure> registerResult = await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);

            if (registerResult.IsErr)
            {
                Log.Error("[ROUTER-PROTOCOL] RegisterDevice failed. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, registerResult.UnwrapErr().Message);
                return;
            }

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
                DeviceRegistrationResponse reply =
                    Helpers.ParseFromBytes<DeviceRegistrationResponse>(decryptedPayload);

                settings.ServerPublicKey = SecureByteStringInterop.WithByteStringAsSpan(reply.ServerPublicKey,
                    ByteString.CopyFrom);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None).ConfigureAwait(false);
    }
}
