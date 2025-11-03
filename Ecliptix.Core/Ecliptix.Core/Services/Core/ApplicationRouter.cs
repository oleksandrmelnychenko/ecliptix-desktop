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
            throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter
                .FailedToCreateMembershipViewModel);
        }

        await mainWindowViewModel.SetAuthenticationContentAsync(membershipViewModel).ConfigureAwait(false);
        await moduleManager.UnloadModuleAsync("Main").ConfigureAwait(false);
        await EnsureAnonymousProtocolAsync().ConfigureAwait(false);
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
        MainWindow mainWindow = await Dispatcher.UIThread.InvokeAsync(() => new MainWindow
        {
            DataContext = mainWindowViewModel
        });

        if (isAuthenticated)
        {
            IModule mainModule = await moduleManager.LoadModuleAsync("Main").ConfigureAwait(false);

            if (mainModule.ServiceScope?.ServiceProvider == null)
            {
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter
                    .FailedToLoadMainModuleFromSplash);
            }

            MasterViewModel? mainViewModel =
                mainModule.ServiceScope.ServiceProvider.GetService<MasterViewModel>();

            if (mainViewModel == null)
            {
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter
                    .FailedToCreateMainViewModel);
            }

            await mainWindowViewModel.SetMainContentAsync(mainViewModel).ConfigureAwait(false);
        }
        else
        {
            IModule authModule = await moduleManager.LoadModuleAsync("Authentication").ConfigureAwait(false);

            if (authModule.ServiceScope?.ServiceProvider == null)
            {
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter
                    .FailedToLoadAuthModuleFromSplash);
            }

            AuthenticationViewModel? membershipViewModel =
                authModule.ServiceScope.ServiceProvider.GetService<AuthenticationViewModel>();

            if (membershipViewModel == null)
            {
                throw new InvalidOperationException(ApplicationErrorMessages.ApplicationRouter
                    .FailedToCreateMembershipViewModel);
            }

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
        await Task.Delay(WindowShowDelayMs).ConfigureAwait(false);
    }

    private async Task PerformFadeTransitionAsync(Window fromWindow, Window toWindow)
    {
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

        await Task.Delay(100).ConfigureAwait(false);
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
        AppDevice appDevice = new()
        {
            AppInstanceId = settings.AppInstanceId,
            DeviceId = settings.DeviceId,
            DeviceType = AppDevice.Types.DeviceType.Desktop
        };

        await networkProvider.ExecuteUnaryRequestAsync(
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
