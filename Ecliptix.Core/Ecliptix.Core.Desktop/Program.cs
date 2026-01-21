using System;
using System.Globalization;
using System.IO;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.ReactiveUI;
using Avalonia.WebView.Desktop;
using DotNetEnv;
using Ecliptix.Core.Modularity.Messaging;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Controls.Modals.OverlaySheetModal;
using Ecliptix.Core.Controls.Modals.SideSheetModal;
using Ecliptix.Core.MVVM;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Desktop.DI;
using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;
using Ecliptix.Core.Controls.TitleBarUtilities.Views;
using Ecliptix.Core.Data.SecureStorage;
using Ecliptix.Core.Data.SecureStorage.Configuration;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Splash;
using Ecliptix.Core.Modularity.Suggestions;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Core.Shell.Configuration;
using Ecliptix.Core.Shell.Factories;
using Ecliptix.Core.Shell.Services;
using Ecliptix.Core.Shell.Services.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Core.Shell.Services.Membership;
using Ecliptix.Feature.Splash.ViewModels;
using Ecliptix.Feature.Suggestions.ViewModels;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Infrastructure.Network.Transport;
using Ecliptix.Network.Infrastructure.Network.Transport.Grpc.Interceptors;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.KeySplitting;
using Ecliptix.Network.Infrastructure.Security.Platform;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Abstractions.Network;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Network.Services.Network;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Security.Certificate.Pinning.Services;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Splat.Microsoft.Extensions.DependencyInjection;
using FeatureRegistration = Ecliptix.Feature.Main.FeatureRegistration;
using IViewLocator = Ecliptix.Core.Modularity.IViewLocator;

namespace Ecliptix.Core.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string mutexName =
            string.Format(ApplicationConstants.ApplicationSettings.MUTEX_NAME_FORMAT, Environment.UserName);
        using Mutex mutex = new(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            return;
        }

        IConfiguration configuration = BuildConfiguration();
        Env.Load();
        Log.Logger = ConfigureSerilog(configuration);

        try
        {
            Log.Information(ApplicationConstants.Logging.STARTUP_MESSAGE);
            IServiceCollection services = ConfigureServices(configuration);

            services.UseMicrosoftDependencyResolver();

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ReactiveUI.IViewLocator reactiveViewLocator = serviceProvider.GetRequiredService<ReactiveUI.IViewLocator>();
            Splat.Locator.CurrentMutable.Register(() => reactiveViewLocator, typeof(ReactiveUI.IViewLocator));
            Splat.Locator.CurrentMutable.Register(() => new LanguageCycleButtonView(),
                typeof(ReactiveUI.IViewFor<LanguageCycleButtonViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new ToggleNavigationSideBarView(),
                typeof(ReactiveUI.IViewFor<ToggleNavigationSideBarViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new ToggleThemeView(),
                typeof(ReactiveUI.IViewFor<ToggleThemeViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new PersonalTagView(),
                typeof(ReactiveUI.IViewFor<PersonalTagViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new EppBadgeView(),
                typeof(ReactiveUI.IViewFor<EppBadgeViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new NetworkBadgeView(),
                typeof(ReactiveUI.IViewFor<NetworkBadgeViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new LanguageMenuButtonView(),
                typeof(ReactiveUI.IViewFor<LanguageMenuButtonViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new VerticalSeparatorView(),
                typeof(ReactiveUI.IViewFor<VerticalSeparatorViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new DetectLanguageDialog(),
                typeof(ReactiveUI.IViewFor<DetectLanguageDialogViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new RedirectNotificationView(),
                typeof(ReactiveUI.IViewFor<RedirectNotificationViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new LanguagePickerView(),
                typeof(ReactiveUI.IViewFor<LanguagePickerViewModel>));
            Splat.Locator.CurrentMutable.Register(() => new CountryCodeView(),
                typeof(ReactiveUI.IViewFor<CountryCodeViewModel>));
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, ApplicationConstants.Logging.FATAL_ERROR_MESSAGE);
            if (configuration[ApplicationConstants.ApplicationSettings.ENVIRONMENT_KEY] !=
                ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT)
            {
                Environment.Exit(ApplicationConstants.ExitCodes.FATAL_ERROR);
            }
        }
        finally
        {
            Log.Information(ApplicationConstants.Logging.SHUTDOWN_MESSAGE);
            Log.CloseAndFlushAsync();
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        string? environment = Env.GetString(ApplicationConstants.ApplicationSettings.DOT_NET_ENVIRONMENT_KEY);
#if DEBUG
        environment ??= ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT;
#else
        environment ??= ApplicationConstants.ApplicationSettings.PRODUCTION_ENVIRONMENT;
#endif

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(ApplicationConstants.Configuration.APP_SETTINGS_FILE, optional: false, reloadOnChange: true)
            .AddJsonFile(
                string.Format(ApplicationConstants.Configuration.ENVIRONMENT_APP_SETTINGS_PATTERN, environment),
                optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static Logger ConfigureSerilog(IConfiguration configuration)
    {
        try
        {
            LoggerConfiguration loggerConfig = new();

            IConfigurationSection serilogSection =
                configuration.GetSection(ApplicationConstants.Configuration.SERILOG_SECTION);

            string minLevel = serilogSection[ApplicationConstants.Configuration.MINIMUM_LEVEL_DEFAULT_KEY] ??
                              ApplicationConstants.LogLevels.INFORMATION;
            loggerConfig = minLevel switch
            {
                ApplicationConstants.LogLevels.DEBUG => loggerConfig.MinimumLevel.Debug(),
                ApplicationConstants.LogLevels.INFORMATION => loggerConfig.MinimumLevel.Information(),
                ApplicationConstants.LogLevels.WARNING => loggerConfig.MinimumLevel.Warning(),
                ApplicationConstants.LogLevels.ERROR => loggerConfig.MinimumLevel.Error(),
                ApplicationConstants.LogLevels.FATAL => loggerConfig.MinimumLevel.Fatal(),
                _ => loggerConfig.MinimumLevel.Information()
            };

            loggerConfig = loggerConfig.WriteTo.Console();

            string logPath = Path.Combine(ApplicationConstants.Storage.LOGS_DIRECTORY,
                ApplicationConstants.Storage.LOG_FILE_PATTERN);
            loggerConfig = loggerConfig.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

            return loggerConfig.CreateLogger();
        }
        catch (Exception)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(ApplicationConstants.Storage.LOGS_DIRECTORY,
                        ApplicationConstants.Storage.LOG_FILE_PATTERN), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }

    private static IServiceCollection ConfigureServices(IConfiguration configuration)
    {
        ServiceCollection services = new();

        ConfigureCoreServices(services, configuration);
        ConfigureNetworkServices(services, configuration);
        ConfigureSecurityServices(services, configuration);
        ConfigureMessagingServices(services);
        ConfigureAuthenticationServices(services);
        ConfigureGrpc(services);
        ConfigureModules(services);

        return services;
    }

    private static string GetSectionValue(IConfigurationSection section, string key, string defaultValue = "") =>
        section[key] ?? defaultValue;

    private static void ConfigureCoreServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationConstants.ApplicationSettings.APPLICATION_NAME)
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath(ApplicationConstants.Storage.DATA_PROTECTION_KEYS_PATH))
            )
            .SetDefaultKeyLifetime(ApplicationConstants.Timeouts.DefaultKeyLifetime);

        services.AddSingleton(configuration);
        services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);

        services.AddSingleton<MainWindowConfiguration>();
        services.AddSingleton<IWindowAnimationService, WindowAnimationService>();
        services.AddSingleton<IWindowPositionService, WindowPositionService>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();
    }

    private static void ConfigureNetworkServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddNetworkInfrastructure(configuration);

    private static void ConfigureSecurityServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOptions<DefaultSystemSettings>>(_ =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.DEFAULT_APP_SETTINGS_SECTION);
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DEFAULT_THEME),
                Environment = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.ENVIRONMENT,
                    ApplicationConstants.ApplicationSettings.PRODUCTION_ENVIRONMENT),
                DataCenterConnectionString = GetSectionValue(section,
                    ApplicationConstants.ConfigurationKeys.DATA_CENTER_CONNECTION_STRING),
                CountryCodeApi = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.COUNTRY_CODE_API),
                DomainName = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DOMAIN_NAME),
                Culture = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.CULTURE),
                PrivacyPolicyUrl = GetSectionValue(section, "PrivacyPolicyUrl"),
                TermsOfServiceUrl = GetSectionValue(section, "TermsOfServiceUrl"),
                SupportUrl = GetSectionValue(section, "SupportUrl")
            };
            return Options.Create(settings);
        });

        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.SECURE_STORE_OPTIONS_SECTION);
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    GetSectionValue(section, ApplicationConstants.ConfigurationKeys.ENCRYPTED_STATE_PATH,
                        ApplicationConstants.Storage.DEFAULT_STATE_PATH)
                )
            };
            return Options.Create(options);
        });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DefaultSystemSettings>>().Value);

        services.AddSingleton<ILogger<ApplicationSecureStorageProvider>>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ApplicationSecureStorageProvider>()
        );
        services.AddSingleton<IApplicationSecureStorageProvider, ApplicationSecureStorageProvider>();

        services.AddSingleton<IPlatformSecurityProvider>(_ =>
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME);
            return new CrossPlatformSecurityProvider(appDataPath);
        });

        services.AddSingleton<ISecureProtocolStateStorage>(sp =>
        {
            IPlatformSecurityProvider platformProvider = sp.GetRequiredService<IPlatformSecurityProvider>();
            IConfiguration config = sp.GetRequiredService<IConfiguration>();

            string storageDirectory =
                config[
                    ApplicationConstants.Configuration.SECURE_STORAGE_SECTION +
                    ApplicationConstants.Configuration.PATH_SEPARATOR +
                    ApplicationConstants.ConfigurationKeys.STATE_PATH]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME);

            byte[] deviceId = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);

            return new SecureProtocolStateStorage(platformProvider, storageDirectory, deviceId);
        });

        services.AddSingleton<ICertificatePinningServiceFactory, CertificatePinningServiceFactory>();
    }

    private static void ConfigureMessagingServices(IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<IGlobalModalService, GlobalModalService>(sp =>
            new GlobalModalService(sp.GetRequiredService<IMessageBus>()));
        services.AddSingleton<ISideSheetService, SideSheetService>();
        services.AddSingleton<IBottomSheetService, BottomSheetService>();
        services.AddSingleton<IOverlaySheetService, OverlaySheetService>();
        services.AddSingleton<IProfileMenuService, ProfileMenuService>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddTransient<ILogoutService, LogoutService>();

        services.AddSingleton<IApplicationStateManager, ApplicationStateManager>();
        services.AddSingleton<IApplicationRouter, ApplicationRouter>();
        services.AddTransient<ApplicationStartup>();
    }

    private static void ConfigureAuthenticationServices(IServiceCollection services)
    {
        services.AddSingleton<IIdentityService, IdentityService>();

        services.AddSingleton<IHardenedKeyDerivation, HardenedKeyDerivation>();

        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();
        services.AddSingleton<IRpcServiceManager, RpcServiceManager>();

        services.AddSingleton<RetryStrategyConfiguration>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            IConfigurationSection section =
                config.GetSection(ApplicationConstants.Configuration.SECRECY_CHANNEL_RETRY_POLICY_SECTION);
            return CreateRetryConfiguration(section);
        });

        services.AddSingleton<IOperationTimeoutProvider, OperationTimeoutProvider>();

        services.AddSingleton<IRetryPolicyProvider>(sp =>
        {
            RetryStrategyConfiguration retryStrategyConfig = sp.GetRequiredService<RetryStrategyConfiguration>();
            return new RetryPolicyProvider(retryStrategyConfig);
        });

        services.AddSingleton<IRetryStrategy>(sp =>
        {
            RetryStrategyConfiguration retryStrategyConfig = sp.GetRequiredService<RetryStrategyConfiguration>();
            IConnectivityService connectivityService = sp.GetRequiredService<IConnectivityService>();
            IOperationTimeoutProvider timeoutProvider = sp.GetRequiredService<IOperationTimeoutProvider>();

            RetryStrategy retryStrategy = new(retryStrategyConfig, connectivityService, timeoutProvider);
            Lazy<NetworkProvider> lazyProvider = new(sp.GetRequiredService<NetworkProvider>);
            retryStrategy.SetLazyNetworkProvider(lazyProvider);
            return retryStrategy;
        });

        services.AddSingleton<IGrpcErrorProcessor, GrpcErrorProcessor>();
        services.AddSingleton<IGrpcDeadlineProvider, GrpcDeadlineProvider>();
        services.AddSingleton<IGrpcCallOptionsFactory, GrpcCallOptionsFactory>();
        services.AddSingleton<IUnaryRpcServices, UnaryRpcServices>();
        services.AddSingleton<ISecrecyChannelRpcServices, SecrecyChannelRpcServices>();
        services.AddSingleton<IReceiveStreamRpcServices, ReceiveStreamRpcServices>();
        services.AddSingleton<IRpcMetaDataProvider, RpcMetaDataProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();
    }

    private static RetryStrategyConfiguration CreateRetryConfiguration(IConfigurationSection section)
    {
        return new RetryStrategyConfiguration
        {
            InitialRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.INITIAL_RETRY_DELAY],
                CultureInfo.InvariantCulture, out TimeSpan initialDelay)
                ? initialDelay
                : ApplicationConstants.Timeouts.DefaultInitialRetryDelay,
            MaxRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.MAX_RETRY_DELAY],
                CultureInfo.InvariantCulture, out TimeSpan maxDelay)
                ? maxDelay
                : ApplicationConstants.Timeouts.DefaultMaxRetryDelay,
            MaxRetries = int.TryParse(section[ApplicationConstants.ConfigurationKeys.MAX_RETRIES], out int maxRetries)
                ? maxRetries
                : ApplicationConstants.Thresholds.DEFAULT_MAX_RETRIES,
            PerAttemptTimeout = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.PER_ATTEMPT_TIMEOUT],
                CultureInfo.InvariantCulture, out TimeSpan perAttemptTimeout)
                ? perAttemptTimeout
                : TimeSpan.FromSeconds(30),
            UseAdaptiveRetry =
                !bool.TryParse(section[ApplicationConstants.ConfigurationKeys.USE_ADAPTIVE_RETRY], out bool adaptive) ||
                adaptive
        };
    }

    private static void ConfigureGrpc(IServiceCollection services)
    {
        services.AddSingleton((Action<GrpcClientFactoryOptions>)ConfigureClientOptions);
        services.AddConfiguredGrpcClients();
        return;

        void ConfigureClientOptions(GrpcClientFactoryOptions options)
        {
            DefaultSystemSettings settings = services.BuildServiceProvider()
                .GetRequiredService<DefaultSystemSettings>();
            string endpoint =
                settings.Environment.Equals(ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT,
                    StringComparison.OrdinalIgnoreCase)
                    ? settings.DataCenterConnectionString
                    : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException(ApplicationConstants.Logging.GRPC_ENDPOINT_ERROR_MESSAGE);
            }

            options.Address = new Uri(endpoint);
        }
    }

    private static void ConfigureModules(IServiceCollection services)
    {
        Action<ModuleServiceContext, IServiceCollection> moduleServiceForwarder = (ctx, moduleServices) =>
        {
            moduleServices.AddSingleton(ctx.GetParentService<IMessageBus>());
            moduleServices.AddSingleton(ctx.GetParentService<IConnectivityService>());
            moduleServices.AddSingleton(ctx.GetParentService<IGlobalModalService>());
            moduleServices.AddSingleton(ctx.GetParentService<NetworkProvider>());
            moduleServices.AddSingleton(ctx.GetParentService<IRpcMetaDataProvider>());
            moduleServices.AddSingleton(ctx.GetParentService<IApplicationSecureStorageProvider>());
            moduleServices.AddSingleton(ctx.GetParentService<ILocalizationService>());
            moduleServices.AddSingleton(ctx.GetParentService<IApplicationRouter>());
            moduleServices.AddSingleton(ctx.GetParentService<IApplicationStateManager>());
            moduleServices.AddSingleton(ctx.GetParentService<ILogoutService>());
            moduleServices.AddSingleton(ctx.GetParentService<IIdentityService>());
            moduleServices.AddSingleton(ctx.GetParentService<IProfileMenuService>());
            moduleServices.AddSingleton(ctx.GetParentService<ILanguageDetectionService>());
            moduleServices.AddSingleton(ctx.GetParentService<IModuleViewFactory>());
            moduleServices.AddSingleton(ctx.GetParentService<Ecliptix.Core.Shell.ViewModels.MainWindowViewModel>());
            moduleServices.AddSingleton(ctx.GetParentService<DefaultSystemSettings>());
        };

        services.AddSingleton(sp => new ModuleResourceManager(sp, moduleServiceForwarder));

        ModuleCatalog catalog = new();
        Feature.Authentication.FeatureRegistration.RegisterModule(catalog);
        FeatureRegistration.RegisterModule(catalog);
        Feature.Feed.FeatureRegistration.RegisterModule(catalog);
        Feature.Chats.FeatureRegistration.RegisterModule(catalog);
        Feature.Settings.FeatureRegistration.RegisterModule(catalog);
        Feature.Profile.FeatureRegistration.RegisterModule(catalog);
        Feature.NewContent.FeatureRegistration.RegisterModule(catalog);

        services.AddSingleton<IModuleCatalog>(catalog);
        services.AddSingleton(catalog);

        services.AddSingleton<IModuleMessageBus, ModuleMessageBus>();
        services.AddSingleton<IModuleManager, ModuleManager>();

        services.AddTransient<SuggestionsViewModel>();
        services.AddTransient<ISuggestionsViewModel, SuggestionsViewModel>();

        services.AddSingleton<LanguagePickerViewModel>();
        services.AddTransient<LanguageCycleButtonViewModel>();
        services.AddSingleton<BottomSheetViewModel>();
        services.AddSingleton<SideSheetViewModel>();
        services.AddSingleton<OverlaySheetViewModel>();
        services.AddSingleton<ConnectivityNotificationViewModel>();
        services.AddSingleton<Ecliptix.Core.Shell.ViewModels.MainWindowViewModel>();
        services.AddTransient<VerticalSeparatorViewModel>();
        services.AddTransient<LanguageMenuButtonViewModel>();
        services.AddTransient<EppBadgeViewModel>();
        services.AddTransient<NetworkBadgeViewModel>();
        services.AddTransient<ToggleNavigationSideBarViewModel>();
        services.AddTransient<ToggleThemeViewModel>();
        services.AddTransient<PersonalTagViewModel>();
        services.AddTransient<SplashWindowViewModel>();
        services.AddSingleton<ISplashHostFactory, Feature.Splash.Services.SplashHostFactory>();

        services.AddSingleton<IViewLocator, ViewLocator>();
        services.AddSingleton<ReactiveUiViewLocatorAdapter>();

        services.AddSingleton<ReactiveUI.IViewLocator>(provider =>
            provider.GetRequiredService<ReactiveUiViewLocatorAdapter>());

        services.AddSingleton<IModuleViewFactory>(provider =>
        {
            ModuleViewFactory factory = new(
                provider.GetRequiredService<IModuleManager>(),
                provider);

            Feature.Authentication.FeatureRegistration.RegisterViews(factory);
            Feature.Main.FeatureRegistration.RegisterViews(factory);
            Feature.Feed.FeatureRegistration.RegisterViews(factory);
            Feature.Chats.FeatureRegistration.RegisterViews(factory);
            Feature.Settings.FeatureRegistration.RegisterViews(factory);
            Feature.Profile.FeatureRegistration.RegisterViews(factory);
            Feature.NewContent.FeatureRegistration.RegisterViews(factory);

            Log.Information("Registered {Count} module views during ModuleViewFactory creation", 7);

            return factory;
        });
    }

    private static string GetPlatformAppDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ApplicationConstants.Storage.LOCAL_SHARE_DIRECTORY
            );
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ApplicationConstants.Storage.APPLICATION_SUPPORT_DIRECTORY
        );
    }

    private static void SetSecurePermissionsIfUnix(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, ApplicationConstants.FilePermissions.SECURE_DIRECTORY_MODE);
            Log.Debug(ApplicationConstants.Logging.PERMISSIONS_SET_MESSAGE, directory);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, ApplicationConstants.Logging.PERMISSIONS_FAIL_MESSAGE, directory);
        }
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException(ApplicationConstants.Logging.PATH_EMPTY_ERROR_MESSAGE, nameof(path));
        }

        string appDataDir = GetPlatformAppDataDirectory();

        path = Environment.ExpandEnvironmentVariables(
            path.Replace(ApplicationConstants.Storage.APP_DATA_ENVIRONMENT_VARIABLE,
                Path.Combine(appDataDir, ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME))
        );

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        SetSecurePermissionsIfUnix(directory);

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().UseReactiveUI().UseDesktopWebView();
}
