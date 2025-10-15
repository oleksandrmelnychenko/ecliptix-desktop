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
        public const string SecrecyChannelRetryPolicySection = "SecrecyChannelRetryPolicy";
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

    public static class ApplicationRouter
    {
        public const string CannotNavigateWindowNull = "Cannot navigate: current window is null";
        public const string FailedToLoadAuthModule = "Failed to load Authentication module";
        public const string FailedToCreateMembershipViewModel = "Failed to create MembershipHostWindowModel";
        public const string FailedToLoadMainModule = "Failed to load Main module";
        public const string FailedToCreateMainViewModel = "Failed to create MainViewModel";
        public const string FailedToLoadMainModuleFromSplash = "Failed to load Main module from splash";
        public const string FailedToLoadAuthModuleFromSplash = "Failed to load Authentication module from splash";
    }

    public static class SecureStorageProvider
    {
        public const string SecureStorageDirectoryCreationFailed = "Could not create secure storage directory: {0}";
        public const string ApplicationSettingsNotFound = "Application instance settings not found.";
        public const string CorruptSettingsData = "Corrupt settings data in secure storage.";
        public const string FailedToEncryptData = "Failed to encrypt data for storage.";
        public const string FailedToWriteToStorage = "Failed to write to secure storage.";
        public const string FailedToDecryptData = "Failed to decrypt data.";
        public const string FailedToAccessStorage = "Failed to access secure storage.";
        public const string FailedToDeleteFromStorage = "Failed to delete from secure storage.";
    }

    public static class SecureProtocolStateStorage
    {
        public const string StorageDisposed = "Storage is disposed";
        public const string StateFileNotFound = "State file not found";
        public const string TamperedStateDetected = "Security violation: tampered state detected";
        public const string AssociatedDataMismatch = "Associated data mismatch";
        public const string InvalidContainerFormat = "Invalid container format";
        public const string UnsupportedVersion = "Unsupported version: {0}";
        public const string SaveFailed = "Save failed: {0}";
        public const string LoadFailed = "Load failed: {0}";
        public const string DeleteFailed = "Delete failed: {0}";
    }
}
