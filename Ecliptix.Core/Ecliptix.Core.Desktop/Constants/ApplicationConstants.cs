using System;
using System.IO;

namespace Ecliptix.Core.Desktop.Constants;

public static class ApplicationConstants
{
    public static class ApplicationSettings
    {
        public const string ApplicationName = "Ecliptix";
        public const string EnvironmentKey = "AppSettings:Environment";
        public const string DevelopmentEnvironment = "Development";
        public const string ProductionEnvironment = "Production";
        public const string DotNetEnvironmentKey = "DOTNET_ENVIRONMENT";
        public const string MutexNameFormat = "EcliptixDesktop_{0}";
    }

    public static class Configuration
    {
        public const string AppSettingsFile = "appsettings.json";
        public const string EnvironmentAppSettingsPattern = "appsettings.{0}.json";
        public const string DefaultAppSettingsSection = "DefaultAppSettings";
        public const string SecureStoreOptionsSection = "SecureStoreOptions";
        public const string SecureStorageSection = "SecureStorage";
        public const string ImprovedRetryPolicySection = "ImprovedRetryPolicy";
        public const string SerilogSection = "Serilog";
        public const string MinimumLevelDefaultKey = "MinimumLevel:Default";
        public const string PathSeparator = ":";
    }

    public static class Storage
    {
        public const string DataProtectionKeysPath = "%APPDATA%/Storage/DataProtection-Keys";
        public const string DefaultStatePath = "Storage/state";
        public const string EcliptixDirectoryName = "Ecliptix";
        public const string LocalShareDirectory = ".local/share";
        public const string ApplicationSupportDirectory = "Library/Application Support";
        public const string LogsDirectory = "logs";
        public const string LogFilePattern = "ecliptix-.log";
        public const string AppDataEnvironmentVariable = "%APPDATA%";
    }

    public static class Timeouts
    {
        public static readonly TimeSpan DefaultKeyLifetime = TimeSpan.FromDays(90);
        public static readonly TimeSpan HttpClientLifetime = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(2);
    }

    public static class Thresholds
    {
        public const int DefaultFailureThreshold = 2;
        public const int DefaultSuccessThreshold = 1;
        public const int DefaultMaxRetries = 10;
        public const int RetryAttempts = 3;
        public const int ExponentialBackoffBase = 2;
    }

    public static class ConfigurationKeys
    {
        public const string DefaultTheme = "DefaultTheme";
        public const string Environment = "Environment";
        public const string DataCenterConnectionString = "DataCenterConnectionString";
        public const string CountryCodeApi = "CountryCodeApi";
        public const string DomainName = "DomainName";
        public const string Culture = "Culture";
        public const string EncryptedStatePath = "EncryptedStatePath";
        public const string StatePath = "StatePath";
        public const string InitialRetryDelay = "InitialRetryDelay";
        public const string MaxRetryDelay = "MaxRetryDelay";
        public const string MaxRetries = "MaxRetries";
        public const string UseAdaptiveRetry = "UseAdaptiveRetry";
    }

    public static class Logging
    {
        public const string StartupMessage = "Starting Ecliptix application...";
        public const string ShutdownMessage = "Application shutting down";
        public const string FatalErrorMessage = "Application terminated unexpectedly during startup or runtime";
        public const string PermissionsSetMessage = "Set secure permissions (700) on directory {Path}";
        public const string PermissionsFailMessage = "Failed to set permissions for directory {Path}";
        public const string GrpcEndpointErrorMessage = "gRPC endpoint URL is not configured in appsettings.json.";
        public const string PathEmptyErrorMessage = "Path cannot be empty.";
    }

    public static class FilePermissions
    {
        public const UnixFileMode SecureDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    }

    public static class ExitCodes
    {
        public const int FatalError = 1;
    }

    public static class LogLevels
    {
        public const string Debug = "Debug";
        public const string Information = "Information";
        public const string Warning = "Warning";
        public const string Error = "Error";
        public const string Fatal = "Fatal";
    }
}
