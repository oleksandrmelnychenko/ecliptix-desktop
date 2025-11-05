namespace Ecliptix.Core.Constants;

public static class ApplicationErrorMessages
{
    public static class ApplicationRouter
    {
        public const string CANNOT_NAVIGATE_WINDOW_NULL = "Cannot navigate: current window is null";
        public const string FAILED_TO_LOAD_AUTH_MODULE = "Failed to load Authentication module";
        public const string FAILED_TO_CREATE_MEMBERSHIP_VIEW_MODEL = "Failed to create AuthenticationViewModel";
        public const string FAILED_TO_LOAD_MAIN_MODULE = "Failed to load Main module";
        public const string FAILED_TO_CREATE_MAIN_VIEW_MODEL = "Failed to create MainViewModel";
        public const string FAILED_TO_LOAD_MAIN_MODULE_FROM_SPLASH = "Failed to load Main module from splash";
        public const string FAILED_TO_LOAD_AUTH_MODULE_FROM_SPLASH = "Failed to load Authentication module from splash";
    }

    public static class SecureStorageProvider
    {
        public const string SECURE_STORAGE_DIRECTORY_CREATION_FAILED = "Could not create secure storage directory: {0}";
        public const string APPLICATION_SETTINGS_NOT_FOUND = "Application instance settings not found.";
        public const string CORRUPT_SETTINGS_DATA = "Corrupt settings data in secure storage.";
        public const string FAILED_TO_ENCRYPT_DATA = "Failed to encrypt data for storage.";
        public const string FAILED_TO_WRITE_TO_STORAGE = "Failed to write to secure storage.";
        public const string FAILED_TO_DECRYPT_DATA = "Failed to decrypt data.";
        public const string FAILED_TO_ACCESS_STORAGE = "Failed to access secure storage.";
        public const string FAILED_TO_DELETE_FROM_STORAGE = "Failed to delete from secure storage.";
    }

    public static class SecureProtocolStateStorage
    {
        public const string STORAGE_DISPOSED = "Storage is disposed";
        public const string STATE_FILE_NOT_FOUND = "State file not found";
        public const string TAMPERED_STATE_DETECTED = "Security violation: tampered state detected";
        public const string ASSOCIATED_DATA_MISMATCH = "Associated data mismatch";
        public const string INVALID_CONTAINER_FORMAT = "Invalid container format";
        public const string UNSUPPORTED_VERSION = "Unsupported version: {0}";
        public const string SAVE_FAILED = "Save failed: {0}";
        public const string LOAD_FAILED = "Load failed: {0}";
        public const string DELETE_FAILED = "Delete failed: {0}";
    }
}
