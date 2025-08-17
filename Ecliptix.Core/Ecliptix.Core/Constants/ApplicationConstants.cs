namespace Ecliptix.Core.Constants;

/// <summary>
/// Application-level constants to avoid magic strings throughout the codebase.
/// </summary>
public static class ApplicationConstants
{
    // Application Settings
    public const string ApplicationName = "Ecliptix";

    // Environment Values
    public const string DevelopmentEnvironment = "Development";
    public const string ProductionEnvironment = "Production";

    // File Names and Paths
    public const string BuildInfoFileName = "build-info.json";
    public const string AppSettingsFileName = "appsettings.json";
    public const string LogsDirectory = "logs";
    public const string LogFilePattern = "ecliptix-.log";
    public const string DefaultStorageStatePath = "Storage/state";
    public const string SecureProtocolStateFileName = "secure_protocol_state.enc";
    public const string AppDataPlaceholder = "%APPDATA%";

    // Directory Paths
    public static class Directories
    {
        public const string LibraryApplicationSupport = "Library/Application Support";
        public const string LocalShare = ".local/share";
        public const string DataProtectionKeys = "DataProtection-Keys";
        public const string Storage = "Storage";
    }

    // Configuration Section Names
    public static class ConfigurationSections
    {
        public const string DefaultAppSettings = "DefaultAppSettings";
        public const string SecureStoreOptions = "SecureStoreOptions";
        public const string ImprovedRetryPolicy = "ImprovedRetryPolicy";
        public const string AppSettings = "AppSettings";
        public const string SecureStorage = "SecureStorage";
    }

    // Configuration Keys
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

        // Retry Policy Keys
        public const string InitialRetryDelay = "InitialRetryDelay";
        public const string MaxRetryDelay = "MaxRetryDelay";
        public const string MaxRetries = "MaxRetries";
        public const string CircuitBreakerThreshold = "CircuitBreakerThreshold";
        public const string CircuitBreakerDuration = "CircuitBreakerDuration";
        public const string RequestDeduplicationWindow = "RequestDeduplicationWindow";
        public const string UseAdaptiveRetry = "UseAdaptiveRetry";
        public const string HealthCheckTimeout = "HealthCheckTimeout";
    }

    // Assembly Names
    public static class AssemblyNames
    {
        public const string SerilogSinksConsole = "Serilog.Sinks.Console";
        public const string SerilogSinksFile = "Serilog.Sinks.File";
    }

    // Version Format
    public const string VersionFormat = "{0}.{1}.{2}";

    // Error Messages
    public static class ErrorMessages
    {
        public const string PathCannotBeEmpty = "Path cannot be empty.";
        public const string GrpcEndpointNotConfigured = "gRPC endpoint URL is not configured in appsettings.json.";
        public const string ApplicationTerminatedUnexpectedly = "Application terminated unexpectedly during startup or runtime";
    }

    // Log Messages
    public static class LogMessages
    {
        public const string StartingApplication = "Starting Ecliptix application...";
        public const string ApplicationShuttingDown = "Application shutting down";
        public const string SetSecurePermissions = "Set secure permissions (700) on directory {Path}";
        public const string FailedToSetPermissions = "Failed to set permissions for directory {Path}";
    }

    // Unix File Permissions
    public const string UnixFilePermissions = "700";

    // Environment Variables
    public const string DotnetEnvironmentVariable = "DOTNET_ENVIRONMENT";

    // Cryptographic Constants
    public static class Cryptographic
    {
        public const int X25519KeySize = 32;
        public const int X25519PrivateKeySize = 32;
        public const int X25519PublicKeySize = 32;
        public const int AesKeySize = 32;
        public const int Ed25519SignatureSize = 64;
        public const int Ed25519SecretKeySize = 64;
        public const int Ed25519PublicKeySize = 32;
        public const int DefaultSaltSize = 16;
        public const int DefaultIterations = 600_000;
        public const char HashSeparator = ':';

        // Hash sizes for different algorithms
        public const int Sha1HashSize = 20;
        public const int Sha256HashSize = 32;
        public const int Sha384HashSize = 48;
        public const int Sha512HashSize = 64;
    }

    // Validation Constants
    public static class Validation
    {
        public const int MinPasswordLength = 6;
        public const int MaxPasswordLength = 21;
        public const int MinCharacterClasses = 2;
        public const double MinEntropyBits = 30.0;
        public const int MinSequenceLength = 4;
        public const int MaxErrorMessageLength = 1000;

        // Password strength score thresholds
        public const int WeakPasswordScore = 2;
        public const int GoodPasswordScore = 4;
        public const int StrongPasswordScore = 6;

        // Character class scoring
        public const int LengthScoreTier1 = 6;
        public const int LengthScoreTier2 = 7;
        public const int LengthScoreTier3 = 9;
        public const int LengthScoreTier4 = 12;
        public const int VarietyBonusThreshold2 = 2;
        public const int VarietyBonusThreshold3 = 3;
        public const int VarietyBonusThreshold4 = 4;

        // Validation limits
        public const int MaxValidationMinutes = 5;
        public const int MaxValidationHour = 1;
        public const int MaxRetriesLimit = 100;
        public const int MaxCircuitBreakerThreshold = 50;
        public const int MaxDeduplicationMinutes = 5;
        public const int MaxHealthCheckMinute = 1;
    }

    // Network and Retry Policy Constants
    public static class Network
    {
        public const int DefaultHttpRetryCount = 3;
        public const int HttpRetryBackoffBase = 2;
        public const int HttpTimeoutSeconds = 5;
        public const int HttpClientHandlerLifetimeMinutes = 5;
        public const int DataProtectionKeyLifetimeDays = 90;

        // Connectivity Observer
        public const int DefaultConnectivityPollingSeconds = 10;
        public const int DefaultFailureThreshold = 2;
        public const int DefaultSuccessThreshold = 1;

        // Default retry configuration values
        public const int DefaultInitialRetryDelaySeconds = 5;
        public const int DefaultMaxRetryDelayMinutes = 2;
        public const int DefaultMaxRetries = 10;
        public const int DefaultCircuitBreakerThreshold = 5;
        public const int DefaultCircuitBreakerDurationMinutes = 1;
        public const int DefaultRequestDeduplicationWindowSeconds = 10;
        public const int DefaultHealthCheckTimeoutSeconds = 5;
    }

    // Protocol Constants
    public static class Protocol
    {
        public const int DefaultCacheWindowSize = 1000;
        public const int InitialIdCounter = 2;
        public const byte IKMPrefixByte = 0xFF;
        public const int RandomIdSize = 4; // sizeof(uint)
    }

    // Icon Resource URIs
    public static class IconResources
    {
        public const string WindowsIconUri = "avares://Ecliptix.Core/Assets/ecliptix.ico";
        public const string MacOSIconUri = "avares://Ecliptix.Core/Assets/EcliptixLogo.icns";
        public const string LinuxIconUri = "avares://Ecliptix.Core/Assets/Ecliptix-logo/logo_256x256.png";
    }

    // API Constants
    public static class Api
    {
        public const string IpGeolocationBaseUrl = "https://api.country.is/";
        public const string JsonMediaType = "application/json";

        // JSON property names for IP geolocation
        public const string IpPropertyName = "ip";
        public const string IpAddressPropertyName = "ipAddress";
        public const string QueryPropertyName = "query";
        public const string CountryPropertyName = "country";
        public const string CountryNamePropertyName = "country_name";
        public const string CountryNameAltPropertyName = "countryName";
        public const string CountryCodePropertyName = "countryCode";
        public const string CountryCodeAltPropertyName = "country_code";
    }

    // External URLs
    public static class ExternalUrls
    {
        public const string PrivacyPolicy = "https://ecliptix.com/privacy";
        public const string TermsOfService = "https://ecliptix.com/terms";
        public const string Support = "https://ecliptix.com/support";
    }

    // Password Validation Messages
    public static class PasswordValidationMessages
    {
        public const string CannotBeEmpty = "Password cannot be empty.";
        public const string MustContainLowercase = "Password must contain at least one lowercase letter.";
        public const string MustContainUppercase = "Password must contain at least one uppercase letter.";
        public const string MustContainDigit = "Password must contain at least one digit.";
        public const string MustContainSpecialCharacter = "Password must contain at least one special character from the set: {0}.";
        public const string MinLengthRequirement = "Password must be at least {0} characters long.";
        public const string InvalidCharactersWithSpecial = "Password contains characters that are not allowed. Only alphanumeric and specified special characters are permitted.";
        public const string InvalidCharactersAlphanumericOnly = "Password contains characters that are not allowed. Only alphanumeric characters are permitted.";
        public const string ComplexityRequirementsNotMet = "Password does not meet complexity requirements: {0}";
    }

    // Navigation Keys
    public static class NavigationKeys
    {
        public const string CreateAccount = "CreateAccount";
        public const string SignIn = "SignIn";
    }
}