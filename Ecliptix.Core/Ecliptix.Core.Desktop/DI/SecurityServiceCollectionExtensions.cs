using System;
using System.IO;
using System.Text;
using Ecliptix.Core.Data.SecureStorage;
using Ecliptix.Core.Data.SecureStorage.Configuration;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Settings;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Platform;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Security.Certificate.Pinning.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Desktop.DI;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddDataProtection()
            .SetApplicationName(ApplicationConstants.ApplicationSettings.APPLICATION_NAME)
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath(ApplicationConstants.Storage.DATA_PROTECTION_KEYS_PATH))
            )
            .SetDefaultKeyLifetime(ApplicationConstants.Timeouts.DefaultKeyLifetime);

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
                    ApplicationConstants.Configuration.PATH_SEPARATOR + ApplicationConstants.ConfigurationKeys.STATE_PATH]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME);

            byte[] deviceId = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);

            return new SecureProtocolStateStorage(platformProvider, storageDirectory, deviceId);
        });

        services.AddSingleton<ICertificatePinningServiceFactory, CertificatePinningServiceFactory>();

        services.AddSingleton(configuration);

        return services;
    }

    private static string GetSectionValue(IConfigurationSection section, string key, string defaultValue = "")
    {
        return section[key] ?? defaultValue;
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

    private static string GetPlatformAppDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (OperatingSystem.IsLinux())
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
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, ApplicationConstants.FilePermissions.SECURE_DIRECTORY_MODE);
        }
        catch (IOException)
        {

        }
    }
}
