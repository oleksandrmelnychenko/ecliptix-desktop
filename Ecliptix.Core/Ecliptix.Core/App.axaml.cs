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
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core;

public record KeyExchangeCompletedMessage;

public partial class App : Application
{
    private readonly CompositeDisposable _disposables = new();
    
    private readonly NetworkController _networkController;
    
    public App()
    {
        _networkController = Locator.Current.GetService<NetworkController>()!;
        
        uint connectId = CreateEcliptixConnectionContext();

        MessageBus.Current.Listen<KeyExchangeCompletedMessage>().Subscribe(message => { })
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
    
    private uint CreateEcliptixConnectionContext()
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;

        uint connectId = ServiceUtilities.ComputeUniqueConnectId(
            appInstanceInfo.AppInstanceId,
            appInstanceInfo.DeviceId, PubKeyExchangeType.DataCenterEphemeralConnect);

        _networkController.CreateEcliptixConnectionContext(connectId, 100, PubKeyExchangeType.DataCenterEphemeralConnect);
        return connectId;
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