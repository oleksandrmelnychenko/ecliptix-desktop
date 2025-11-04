using System;
using System.IO;

namespace Ecliptix.Core.Desktop.Constants;

public static class ApplicationConstants
{
    public static class ApplicationSettings
    {
        public const string APPLICATION_NAME = "Ecliptix";
        public const string ENVIRONMENT_KEY = "AppSettings:ENVIRONMENT";
        public const string DEVELOPMENT_ENVIRONMENT = "Development";
        public const string PRODUCTION_ENVIRONMENT = "Production";
        public const string DOT_NET_ENVIRONMENT_KEY = "DOTNET_ENVIRONMENT";
        public const string MUTEX_NAME_FORMAT = "EcliptixDesktop_{0}";
    }

    public static class Configuration
    {
        public const string APP_SETTINGS_FILE = "appsettings.json";
        public const string ENVIRONMENT_APP_SETTINGS_PATTERN = "appsettings.{0}.json";
        public const string DEFAULT_APP_SETTINGS_SECTION = "DefaultAppSettings";
        public const string SECURE_STORE_OPTIONS_SECTION = "SecureStoreOptions";
        public const string SECURE_STORAGE_SECTION = "SecureStorage";
        public const string SECRECY_CHANNEL_RETRY_POLICY_SECTION = "SecrecyChannelRetryPolicy";
        public const string SERILOG_SECTION = "Serilog";
        public const string MINIMUM_LEVEL_DEFAULT_KEY = "MinimumLevel:Default";
        public const string PATH_SEPARATOR = ":";
    }

    public static class Storage
    {
        public const string DATA_PROTECTION_KEYS_PATH = "%APPDATA%/Storage/DataProtection-Keys";
        public const string DEFAULT_STATE_PATH = "Storage/state";
        public const string ECLIPTIX_DIRECTORY_NAME = "Ecliptix";
        public const string LOCAL_SHARE_DIRECTORY = ".local/share";
        public const string APPLICATION_SUPPORT_DIRECTORY = "LIBRARY/Application Support";
        public const string LOGS_DIRECTORY = "logs";
        public const string LOG_FILE_PATTERN = "ecliptix-.log";
        public const string APP_DATA_ENVIRONMENT_VARIABLE = "%APPDATA%";
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
        public const int DEFAULT_FAILURE_THRESHOLD = 2;
        public const int DEFAULT_SUCCESS_THRESHOLD = 1;
        public const int DEFAULT_MAX_RETRIES = 10;
        public const int RETRY_ATTEMPTS = 3;
        public const int EXPONENTIAL_BACKOFF_BASE = 2;
    }

    public static class ConfigurationKeys
    {
        public const string DEFAULT_THEME = "DEFAULT_THEME";
        public const string ENVIRONMENT = "ENVIRONMENT";
        public const string DATA_CENTER_CONNECTION_STRING = "DATA_CENTER_CONNECTION_STRING";
        public const string COUNTRY_CODE_API = "COUNTRY_CODE_API";
        public const string DOMAIN_NAME = "DOMAIN_NAME";
        public const string CULTURE = "CULTURE";
        public const string ENCRYPTED_STATE_PATH = "ENCRYPTED_STATE_PATH";
        public const string STATE_PATH = "STATE_PATH";
        public const string INITIAL_RETRY_DELAY = "INITIAL_RETRY_DELAY";
        public const string MAX_RETRY_DELAY = "MAX_RETRY_DELAY";
        public const string MAX_RETRIES = "MAX_RETRIES";
        public const string PER_ATTEMPT_TIMEOUT = "PER_ATTEMPT_TIMEOUT";
        public const string USE_ADAPTIVE_RETRY = "USE_ADAPTIVE_RETRY";
    }

    public static class Logging
    {
        public const string STARTUP_MESSAGE = "Starting Ecliptix application...";
        public const string SHUTDOWN_MESSAGE = "Application shutting down";
        public const string FATAL_ERROR_MESSAGE = "Application terminated unexpectedly during startup or runtime";
        public const string PERMISSIONS_SET_MESSAGE = "Set secure permissions (700) on directory {Path}";
        public const string PERMISSIONS_FAIL_MESSAGE = "Failed to set permissions for directory {Path}";
        public const string GRPC_ENDPOINT_ERROR_MESSAGE = "gRPC endpoint URL is not configured in appsettings.json.";
        public const string PATH_EMPTY_ERROR_MESSAGE = "Path cannot be empty.";
    }

    public static class FilePermissions
    {
        public const UnixFileMode SECURE_DIRECTORY_MODE =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    }

    public static class ExitCodes
    {
        public const int FATAL_ERROR = 1;
    }

    public static class LogLevels
    {
        public const string DEBUG = "DEBUG";
        public const string INFORMATION = "INFORMATION";
        public const string WARNING = "WARNING";
        public const string ERROR = "ERROR";
        public const string FATAL = "FATAL";
    }
}
