using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Ecliptix.Core.Network;
using Ecliptix.Core.OpaqueProtocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Authentication;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC;
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
        
        ConfigureServices();
    
        // Test secure storage
        _ = TestSecureStorageAsync();

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

        Result<Unit, EcliptixProtocolFailure> result = await _networkController.DataCenterPubKeyExchange(connectId);
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
                    appInstanceInfo.ServerPublicKey = reply.ServerPublicKey.ToByteArray();
                }

                _logger.LogInformation("Device registered with ID: {AppServerInstanceId}", appServerInstanceId);

                return Task.FromResult(Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
            }, token);
    }
    private void ConfigureServices()
    {
        // Configure the SecureStoreOptions
        var secureStoreOptions = new SecureStoreOptions
        {
            StorePath = Path.Combine(AppContext.BaseDirectory, "secure_data.bin"),
            KeyPath = Path.Combine(AppContext.BaseDirectory, "secure_data.key")
        };
    
        // Register the options and service
        Locator.CurrentMutable.RegisterConstant(
            Options.Create(secureStoreOptions), 
            typeof(IOptions<SecureStoreOptions>));
    
        // Register the IBytesStorageService with the SecureByteStorageService implementation
        Locator.CurrentMutable.RegisterLazySingleton<IBytesStorageService>(() => 
            new SecureByteStorageService(
                Locator.Current.GetService<ILogger<SecureByteStorageService>>(), 
                Locator.Current.GetService<IOptions<SecureStoreOptions>>()));
    }
    
 private async Task TestSecureStorageAsync()
{
    try
    {
        var bytesStorage = Locator.Current.GetService<IBytesStorageService>();
        if (bytesStorage == null)
        {
            Console.WriteLine("Error: IBytesStorageService not registered");
            return;
        }

        // Get the options to print file paths
        var options = Locator.Current.GetService<IOptions<SecureStoreOptions>>();
        string storePath = options.Value.StorePath;
        string keyPath = options.Value.KeyPath;
        
        Console.WriteLine($"Store path: {Path.GetFullPath(storePath)}");
        Console.WriteLine($"Key path: {Path.GetFullPath(keyPath)}");

        // Ensure directories exist
        string storeDir = Path.GetDirectoryName(storePath);
        string keyDir = Path.GetDirectoryName(keyPath);
        if (!Directory.Exists(storeDir)) Directory.CreateDirectory(storeDir);
        if (!Directory.Exists(keyDir)) Directory.CreateDirectory(keyDir);
        Console.WriteLine($"Ensured directories exist: {storeDir}, {keyDir}");

        Console.WriteLine("Testing SecureByteStorageService...");

        // Test data
        string testKey = "test_key";
        
        try
        {
            // First, check for existing data on startup
            Console.WriteLine($"Checking for existing data with key '{testKey}'");
            byte[] existingData = await bytesStorage.RetrieveAsync(testKey);
            if (existingData != null)
            {
                string existingMessage = System.Text.Encoding.UTF8.GetString(existingData);
                Console.WriteLine($"Found existing message: {existingMessage}");
            }
            else
            {
                Console.WriteLine("No existing data found");
            }
            
            // Store new test data
            byte[] testData = System.Text.Encoding.UTF8.GetBytes($"This is a test secure message! Created at: {DateTime.Now}");
            Console.WriteLine($"Attempting to store {testData.Length} bytes of data");
            bool storeResult = await bytesStorage.StoreAsync(testKey, testData);
            Console.WriteLine($"Store operation result: {storeResult}");
            
            if (!storeResult) 
            {
                Console.WriteLine("Store operation failed - check if the SecureByteStorageService is properly implemented");
            }

            // Try to read the data
            byte[] retrievedData = await bytesStorage.RetrieveAsync(testKey);
            if (retrievedData != null)
            {
                string retrievedMessage = System.Text.Encoding.UTF8.GetString(retrievedData);
                Console.WriteLine($"Retrieved message: {retrievedMessage}");
            }
            else
            {
                Console.WriteLine("Failed to retrieve data - check storage implementation");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Operation error: {ex.Message}");
            Console.WriteLine($"Inner exception: {ex.InnerException?.Message}");
        }

        Console.WriteLine("SecureByteStorageService test completed!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error testing secure storage: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}
}