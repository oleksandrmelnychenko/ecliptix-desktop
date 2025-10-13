namespace Ecliptix.Core.Constants;

public static class ApplicationErrorMessages
{
    public static class ApplicationRouter
    {
        public const string CannotNavigateWindowNull = "Cannot navigate: current window is null";
        public const string FailedToLoadAuthModule = "Failed to load Authentication module";
        public const string FailedToCreateMembershipViewModel = "Failed to create AuthenticationViewModel";
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
