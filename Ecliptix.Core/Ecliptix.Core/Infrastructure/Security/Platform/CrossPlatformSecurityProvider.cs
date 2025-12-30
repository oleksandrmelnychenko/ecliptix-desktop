using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.Platform;

internal sealed class CrossPlatformSecurityProvider : IPlatformSecurityProvider
{
    private const int AES_KEY_SIZE = 32;
    private const int AES_IV_SIZE = 16;
    private const int GCM_NONCE_SIZE = 12;
    private const int GCM_TAG_SIZE = 16;
    private const int HMAC_KEY_SIZE = 64;
    private const int PBKDF_2_ITERATIONS = 100000;
    private const int SECURE_OVERWRITE_SIZE = 1024;
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int CRED_MAX_CREDENTIAL_BLOB_SIZE = 512;
    private const string MACHINE_KEY_SALT = "EcliptixMachineKey";
    private const string HMAC_KEY_IDENTIFIER = "ecliptix_hmac_key";
    private const string MACHINE_KEY_IDENTIFIER = "ecliptix_machine_key";
    private const string MACHINE_SALT_IDENTIFIER = "ecliptix_machine_salt";
    private const int RANDOM_SALT_SIZE = 32;
    private const string MACHINE_KEY_FILENAME = ".machine.key";
    private const string KEYCHAIN_FOLDER = ".keychain";
    private const string KEYCHAIN_SERVICE_NAME = "com.ecliptix.desktop";
    private const string KEYCHAIN_TARGET_PREFIX = "ecliptix";
    private const string TPM_REGISTRY_PATH = @"SYSTEM\CurrentControlSet\Services\TPM\";
    private const string LINUX_MACHINE_ID_PATH = "/etc/machine-id";
    private const string LINUX_TPM_PATH = "/dev/tpm0";
    private const string LINUX_TPMRM_PATH = "/dev/tpmrm0";
    private const string MACOS_UUID_PATTERN = @"IOPlatformUUID""\s*=\s*""([^""]+)""";

    private static readonly byte[] KeyFileMagic = Encoding.ASCII.GetBytes("ECLXKEY2");

    private readonly string _keychainPath;
    private readonly Lock _lockObject = new();

    private byte[]? _cachedMachineKey;
    private byte[]? _cachedHmacKey;
    private bool _disposed;

    public CrossPlatformSecurityProvider(string appDataPath)
    {
        _keychainPath = Path.Combine(appDataPath, KEYCHAIN_FOLDER);
        InitializeKeychain();
    }

    private void InitializeKeychain()
    {
        if (Directory.Exists(_keychainPath))
        {
            return;
        }

        Directory.CreateDirectory(_keychainPath);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(_keychainPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public async Task<byte[]> GenerateSecureRandomAsync(int length)
    {
        return await Task.Run(() =>
        {
            try
            {
                byte[] bytes = RandomNumberGenerator.GetBytes(length);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    TryEnhanceWithHardwareRandom(bytes);
                }

                return bytes;
            }
            catch (Exception)
            {
                return RandomNumberGenerator.GetBytes(length);
            }
        });
    }

    public async Task StoreKeyInKeychainAsync(string identifier, byte[] key)
    {
        await Task.Run(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    Result<Unit, SecureStorageFailure> result = GetPlatformStore(identifier, key);
                    if (result.IsErr)
                    {
                        Log.Warning("[KEYCHAIN-STORE-PLATFORM] Platform store failed: {Error}, falling back to encrypted file", result.UnwrapErr().Message);
                        Result<Unit, SecureStorageFailure> fileResult = StoreInEncryptedFile(identifier, key);
                        if (fileResult.IsErr)
                        {
                            Log.Error("[KEYCHAIN-STORE-FILE] Encrypted file store FAILED: {Error}", fileResult.UnwrapErr().Message);
                            throw new InvalidOperationException($"Failed to store key: {fileResult.UnwrapErr().Message}");
                        }
                    }
                    else
                    {
                        TryDeleteKeyFile(identifier);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[KEYCHAIN-STORE-EXCEPTION] Exception during store, attempting encrypted file fallback");
                    Result<Unit, SecureStorageFailure> fileResult = StoreInEncryptedFile(identifier, key);
                    if (fileResult.IsErr)
                    {
                        Log.Error("[KEYCHAIN-STORE-FILE] Encrypted file store FAILED: {Error}", fileResult.UnwrapErr().Message);
                        throw new InvalidOperationException($"Failed to store key: {fileResult.UnwrapErr().Message}", ex);
                    }
                }
            }
        });
    }

    private Result<Unit, SecureStorageFailure> GetPlatformStore(string identifier, byte[] key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return StoreInWindowsCredentialManager(identifier, key);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return StoreInMacOsKeychain(identifier, key);
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StoreInLinuxSecretService(identifier, key)
            : StoreInEncryptedFile(identifier, key);
    }

    public async Task<byte[]?> GetKeyFromKeychainAsync(string identifier)
    {
        return await Task.Run(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    Result<byte[], SecureStorageFailure> result = GetPlatformRetrieve(identifier);
                    if (!result.IsErr)
                    {
                        return result.IsOk ? result.Unwrap() : null;
                    }

                    Result<byte[], SecureStorageFailure> fileResult = GetFromEncryptedFile(identifier);
                    if (fileResult.IsOk)
                    {
                        byte[] key = fileResult.Unwrap();
                        Result<Unit, SecureStorageFailure> migrateResult = GetPlatformStore(identifier, key);
                        if (!migrateResult.IsErr)
                        {
                            TryDeleteKeyFile(identifier);
                        }
                        return key;
                    }
                    return null;
                }
                catch (Exception)
                {
                    Result<byte[], SecureStorageFailure> fileResult = GetFromEncryptedFile(identifier);
                    if (fileResult.IsOk)
                    {
                        byte[] key = fileResult.Unwrap();
                        Result<Unit, SecureStorageFailure> migrateResult = GetPlatformStore(identifier, key);
                        if (!migrateResult.IsErr)
                        {
                            TryDeleteKeyFile(identifier);
                        }
                        return key;
                    }
                    return null;
                }
            }
        });
    }

    private Result<byte[], SecureStorageFailure> GetPlatformRetrieve(string identifier)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetFromWindowsCredentialManager(identifier);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetFromMacOsKeychain(identifier);
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? GetFromLinuxSecretService(identifier)
            : GetFromEncryptedFile(identifier);
    }

    private Result<Unit, SecureStorageFailure> DeleteFromPlatformStore(string identifier)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DeleteFromWindowsCredentialManager(identifier);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return DeleteFromMacOsKeychain(identifier);
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? DeleteFromLinuxSecretService(identifier)
            : Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure("Unsupported platform"));
    }

    public async Task DeleteKeyFromKeychainAsync(string identifier)
    {
        await Task.Run(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    Result<Unit, SecureStorageFailure> platformDelete = DeleteFromPlatformStore(identifier);
                    if (platformDelete.IsErr)
                    {
                        Log.Debug("[KEYCHAIN-DELETE] Platform delete failed: {Error}",
                            platformDelete.UnwrapErr().Message);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[KEYCHAIN-DELETE] Platform delete threw exception");
                }

                TryDeleteKeyFile(identifier);
            }
        });
    }

    public async Task<byte[]> GetOrCreateHmacKeyAsync()
    {
        if (_cachedHmacKey != null)
        {
            // Return a clone to prevent callers from modifying the cached key
            return (byte[])_cachedHmacKey.Clone();
        }

        byte[]? hmacKey = await GetKeyFromKeychainAsync(HMAC_KEY_IDENTIFIER);

        if (hmacKey == null)
        {
            byte[] newKey = await GenerateSecureRandomAsync(HMAC_KEY_SIZE);
            await StoreKeyInKeychainAsync(HMAC_KEY_IDENTIFIER, newKey);

            _cachedHmacKey = newKey;
            // Return a clone to prevent callers from modifying the cached key
            return (byte[])_cachedHmacKey.Clone();
        }

        _cachedHmacKey = hmacKey;
        // Return a clone to prevent callers from modifying the cached key
        return (byte[])_cachedHmacKey.Clone();
    }

    public bool IsHardwareSecurityAvailable()
    {
        return IsPlatformKeychainAvailable();
    }

    private static bool IsPlatformKeychainAvailable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return true;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && LinuxSecretService.IsAvailable;
    }

    public async Task<Option<byte[]>> HardwareEncryptAsync(byte[] data)
    {
        byte[] key = await GetOrCreateHmacKeyAsync();
        byte[]? aesKey = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? ciphertext = null;

        try
        {
            aesKey = key.AsSpan(0, AES_KEY_SIZE).ToArray();
            nonce = RandomNumberGenerator.GetBytes(GCM_NONCE_SIZE);
            tag = new byte[GCM_TAG_SIZE];
            ciphertext = new byte[data.Length];

            using AesGcm aes = new(aesKey);
            aes.Encrypt(nonce, data, ciphertext, tag);

            byte[] result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

            return Option<byte[]>.Some(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware encryption failed");
            return Option<byte[]>.None;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (aesKey != null)
            {
                CryptographicOperations.ZeroMemory(aesKey);
            }

            if (nonce != null)
            {
                CryptographicOperations.ZeroMemory(nonce);
            }

            if (tag != null)
            {
                CryptographicOperations.ZeroMemory(tag);
            }

            if (ciphertext != null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
    }

    public async Task<Option<byte[]>> HardwareDecryptAsync(byte[] data)
    {
        if (data.Length < GCM_NONCE_SIZE + GCM_TAG_SIZE + 1)
        {
            Log.Error(
                "[HARDWARE-DECRYPT-ERROR] Invalid encrypted data format - data too small. Length: {Length}, MinSize: {MinSize}",
                data.Length, GCM_NONCE_SIZE + GCM_TAG_SIZE + 1);
            return Option<byte[]>.None;
        }

        byte[] key = await GetOrCreateHmacKeyAsync();
        byte[]? aesKey = null;

        try
        {
            aesKey = key.AsSpan(0, AES_KEY_SIZE).ToArray();

            ReadOnlySpan<byte> nonce = data.AsSpan(0, GCM_NONCE_SIZE);
            ReadOnlySpan<byte> tag = data.AsSpan(GCM_NONCE_SIZE, GCM_TAG_SIZE);
            ReadOnlySpan<byte> ciphertext = data.AsSpan(GCM_NONCE_SIZE + GCM_TAG_SIZE);

            byte[] plaintext = new byte[ciphertext.Length];
            using AesGcm aes = new(aesKey);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Option<byte[]>.Some(plaintext);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HARDWARE-DECRYPT-ERROR] Hardware decryption failed: {Error}", ex.Message);
            return Option<byte[]>.None;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (aesKey != null)
            {
                CryptographicOperations.ZeroMemory(aesKey);
            }
        }
    }

    private string BuildKeychainTarget(string identifier) =>
        $"{KEYCHAIN_TARGET_PREFIX}:{BuildKeychainIdentifier(identifier)}";

    private string BuildKeychainAccount(string identifier) =>
        BuildKeychainIdentifier(identifier);

    private Result<Unit, SecureStorageFailure> StoreInWindowsCredentialManager(string identifier, byte[] key)
    {
        if (key.Length > CRED_MAX_CREDENTIAL_BLOB_SIZE)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Credential too large ({key.Length} bytes)"));
        }

        string target = BuildKeychainTarget(identifier);
        IntPtr credentialBlob = Marshal.AllocHGlobal(key.Length);
        try
        {
            Marshal.Copy(key, 0, credentialBlob, key.Length);
            CREDENTIAL credential = new()
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = (uint)key.Length,
                CredentialBlob = credentialBlob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = KEYCHAIN_SERVICE_NAME
            };

            if (!CredWrite(ref credential, 0))
            {
                int error = Marshal.GetLastWin32Error();
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure($"CredWrite failed: {error}"));
            }

            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(credentialBlob);
        }
    }

    private Result<byte[], SecureStorageFailure> GetFromWindowsCredentialManager(string identifier)
    {
        string target = BuildKeychainTarget(identifier);
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr credentialPtr))
        {
            int error = Marshal.GetLastWin32Error();
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure($"CredRead failed: {error}"));
        }

        try
        {
            CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Credential blob missing"));
            }

            int length = checked((int)credential.CredentialBlobSize);
            byte[] key = new byte[length];
            Marshal.Copy(credential.CredentialBlob, key, 0, length);
            return Result<byte[], SecureStorageFailure>.Ok(key);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private Result<Unit, SecureStorageFailure> DeleteFromWindowsCredentialManager(string identifier)
    {
        string target = BuildKeychainTarget(identifier);
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168)
            {
                return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
            }
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"CredDelete failed: {error}"));
        }

        return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
    }

    private Result<Unit, SecureStorageFailure> StoreInMacOsKeychain(string identifier, byte[] key)
    {
        string account = BuildKeychainAccount(identifier);
        int status = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)KEYCHAIN_SERVICE_NAME.Length,
            KEYCHAIN_SERVICE_NAME,
            (uint)account.Length,
            account,
            (uint)key.Length,
            key,
            out IntPtr itemRef);

        if (itemRef != IntPtr.Zero)
        {
            CFRelease(itemRef);
        }

        if (status == MacOsKeychainErrors.DuplicateItem)
        {
            Result<Unit, SecureStorageFailure> deleteResult = DeleteFromMacOsKeychain(identifier);
            if (deleteResult.IsErr)
            {
                return deleteResult;
            }

            status = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)KEYCHAIN_SERVICE_NAME.Length,
                KEYCHAIN_SERVICE_NAME,
                (uint)account.Length,
                account,
                (uint)key.Length,
                key,
                out itemRef);

            if (itemRef != IntPtr.Zero)
            {
                CFRelease(itemRef);
            }
        }

        return status == 0
            ? Result<Unit, SecureStorageFailure>.Ok(Unit.Value)
            : Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Keychain add failed: {status}"));
    }

    private Result<byte[], SecureStorageFailure> GetFromMacOsKeychain(string identifier)
    {
        string account = BuildKeychainAccount(identifier);
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)KEYCHAIN_SERVICE_NAME.Length,
            KEYCHAIN_SERVICE_NAME,
            (uint)account.Length,
            account,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        if (status != 0)
        {
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure($"Keychain lookup failed: {status}"));
        }

        try
        {
            byte[] key = new byte[passwordLength];
            Marshal.Copy(passwordData, key, 0, checked((int)passwordLength));
            return Result<byte[], SecureStorageFailure>.Ok(key);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
            {
                CFRelease(itemRef);
            }
        }
    }

    private Result<Unit, SecureStorageFailure> DeleteFromMacOsKeychain(string identifier)
    {
        string account = BuildKeychainAccount(identifier);
        int status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)KEYCHAIN_SERVICE_NAME.Length,
            KEYCHAIN_SERVICE_NAME,
            (uint)account.Length,
            account,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        if (status == MacOsKeychainErrors.ItemNotFound)
        {
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }

        if (status != 0)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Keychain lookup failed: {status}"));
        }

        try
        {
            status = SecKeychainItemDelete(itemRef);
            return status == 0
                ? Result<Unit, SecureStorageFailure>.Ok(Unit.Value)
                : Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure($"Keychain delete failed: {status}"));
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
            {
                CFRelease(itemRef);
            }
        }
    }

    private Result<Unit, SecureStorageFailure> StoreInLinuxSecretService(string identifier, byte[] key) =>
        LinuxSecretService.Store(BuildKeychainAccount(identifier), key);

    private Result<byte[], SecureStorageFailure> GetFromLinuxSecretService(string identifier) =>
        LinuxSecretService.Get(BuildKeychainAccount(identifier));

    private Result<Unit, SecureStorageFailure> DeleteFromLinuxSecretService(string identifier) =>
        LinuxSecretService.Delete(BuildKeychainAccount(identifier));

    private Result<Unit, SecureStorageFailure> StoreInEncryptedFile(string identifier, byte[] key)
    {
        byte[]? machineKey = null;
        byte[]? associatedData = null;

        try
        {
            string keyFile = GetKeyFilePath(identifier);

            Option<byte[]> machineKeyOpt = GetMachineKey();
            if (!machineKeyOpt.IsSome)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to get machine key"));
            }
            machineKey = machineKeyOpt.Value!;
            associatedData = BuildAssociatedData(identifier);

            WriteEncryptedKeyFile(keyFile, key, machineKey, associatedData);
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Failed to store encrypted file: {ex.Message}"));
        }
        finally
        {
            if (machineKey != null)
            {
                CryptographicOperations.ZeroMemory(machineKey);
            }

            if (associatedData != null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }
        }
    }

    private Result<byte[], SecureStorageFailure> GetFromEncryptedFile(string identifier)
    {
        string keyFile = GetKeyFilePath(identifier);

        if (!File.Exists(keyFile))
        {
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure("Key file not found"));
        }

        byte[]? machineKey = null;
        byte[]? associatedData = null;

        try
        {
            byte[] encrypted = File.ReadAllBytes(keyFile);

            Option<byte[]> machineKeyOpt = GetMachineKey();
            if (!machineKeyOpt.IsSome)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to get machine key"));
            }
            machineKey = machineKeyOpt.Value!;
            associatedData = BuildAssociatedData(identifier);

            byte[] decrypted = DecryptKeyFile(encrypted, machineKey, associatedData, out bool wasLegacy);
            if (wasLegacy)
            {
                TryUpgradeKeyFile(keyFile, decrypted, machineKey, associatedData);
            }

            return Result<byte[], SecureStorageFailure>.Ok(decrypted);
        }
        catch (CryptographicException)
        {
            try
            {
                File.Delete(keyFile);
            }
            catch (Exception deleteEx)
            {
                Log.Error(deleteEx,
                    "[KEYCHAIN-FILE-DELETE-ERROR] Failed to delete corrupted key file. Identifier: {Identifier}, Path: {Path}, ERROR: {Error}",
                    identifier, keyFile, deleteEx.Message);
            }

            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure("Decryption failed - corrupted or wrong key"));
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "[KEYCHAIN-FILE-ERROR] Failed to read encrypted file. Identifier: {Identifier}, Path: {Path}, ERROR: {Error}",
                identifier, keyFile, ex.Message);
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure($"Failed to read encrypted file: {ex.Message}"));
        }
        finally
        {
            if (machineKey != null)
            {
                CryptographicOperations.ZeroMemory(machineKey);
            }

            if (associatedData != null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }
        }
    }

    private static byte[] BuildAssociatedData(string identifier)
    {
        string hashedIdentifier = HashIdentifier(identifier);
        return Encoding.UTF8.GetBytes(hashedIdentifier);
    }

    private static byte[] BuildAssociatedDataFromHash(string hashedIdentifier) =>
        Encoding.UTF8.GetBytes(hashedIdentifier);

    private static bool HasKeyFileMagic(ReadOnlySpan<byte> data) =>
        data.Length > KeyFileMagic.Length && data[..KeyFileMagic.Length].SequenceEqual(KeyFileMagic);

    private static byte[] DecryptKeyFile(ReadOnlySpan<byte> encrypted, byte[] machineKey, byte[] associatedData,
        out bool wasLegacy)
    {
        if (HasKeyFileMagic(encrypted))
        {
            wasLegacy = false;
            int headerSize = KeyFileMagic.Length;
            int minSize = headerSize + GCM_NONCE_SIZE + GCM_TAG_SIZE + 1;
            if (encrypted.Length < minSize)
            {
                throw new CryptographicException("Invalid encrypted file format (AEAD too small)");
            }

            ReadOnlySpan<byte> nonce = encrypted.Slice(headerSize, GCM_NONCE_SIZE);
            ReadOnlySpan<byte> tag = encrypted.Slice(headerSize + GCM_NONCE_SIZE, GCM_TAG_SIZE);
            ReadOnlySpan<byte> ciphertext = encrypted.Slice(headerSize + GCM_NONCE_SIZE + GCM_TAG_SIZE);

            byte[] plaintext = new byte[ciphertext.Length];
            using AesGcm aes = new(machineKey);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

            return plaintext;
        }

        wasLegacy = true;
        if (encrypted.Length <= AES_IV_SIZE)
        {
            throw new CryptographicException("Invalid encrypted file format (legacy too small)");
        }

        using Aes aesLegacy = Aes.Create();
        aesLegacy.Key = machineKey;

        ReadOnlySpan<byte> iv = encrypted[..AES_IV_SIZE];
        ReadOnlySpan<byte> ciphertextLegacy = encrypted[AES_IV_SIZE..];
        aesLegacy.IV = iv.ToArray();

        using ICryptoTransform decryptor = aesLegacy.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertextLegacy.ToArray(), 0, ciphertextLegacy.Length);
    }

    private static void WriteEncryptedKeyFile(string keyFile, byte[] keyMaterial, byte[] machineKey,
        byte[] associatedData)
    {
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? ciphertext = null;
        byte[]? payload = null;

        try
        {
            nonce = RandomNumberGenerator.GetBytes(GCM_NONCE_SIZE);
            tag = new byte[GCM_TAG_SIZE];
            ciphertext = new byte[keyMaterial.Length];

            using AesGcm aes = new(machineKey);
            aes.Encrypt(nonce, keyMaterial, ciphertext, tag, associatedData);

            payload = new byte[KeyFileMagic.Length + nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(KeyFileMagic, 0, payload, 0, KeyFileMagic.Length);
            Buffer.BlockCopy(nonce, 0, payload, KeyFileMagic.Length, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, KeyFileMagic.Length + nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, payload, KeyFileMagic.Length + nonce.Length + tag.Length,
                ciphertext.Length);

            using FileStream fs = new(keyFile, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(payload, 0, payload.Length);
            fs.Flush(true);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (nonce != null)
            {
                CryptographicOperations.ZeroMemory(nonce);
            }

            if (tag != null)
            {
                CryptographicOperations.ZeroMemory(tag);
            }

            if (ciphertext != null)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }

            if (payload != null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static void TryUpgradeKeyFile(string keyFile, byte[] keyMaterial, byte[] machineKey,
        byte[] associatedData)
    {
        try
        {
            WriteEncryptedKeyFile(keyFile, keyMaterial, machineKey, associatedData);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[KEYCHAIN-FILE-UPGRADE] Failed to upgrade legacy key file: {Path}", keyFile);
        }
    }

    private string GetKeyFilePath(string identifier)
    {
        string safeIdentifier = HashIdentifier(identifier);
        return Path.Combine(_keychainPath, $"{safeIdentifier}.key");
    }

    private string BuildKeychainIdentifier(string identifier) => HashIdentifier(identifier);

    private void TryDeleteKeyFile(string identifier)
    {
        string keyFile = GetKeyFilePath(identifier);
        TrySecureDeleteFile(keyFile);
    }

    private static void TrySecureDeleteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            // Get the actual file size to ensure complete overwrite
            FileInfo fileInfo = new(filePath);
            long fileSize = fileInfo.Length;

            // Overwrite with random data at least the size of the file
            // Use minimum of SECURE_OVERWRITE_SIZE to handle edge cases
            int overwriteSize = (int)Math.Max(fileSize, SECURE_OVERWRITE_SIZE);
            byte[] randomData = RandomNumberGenerator.GetBytes(overwriteSize);

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
                fs.Write(randomData, 0, randomData.Length);
                fs.Flush(true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(randomData);
            }

            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[KEYCHAIN-CLEANUP] Could not securely delete file: {FilePath}", filePath);

            // Try simple delete as fallback
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Best effort - ignore
            }
        }
    }

    private static string HashIdentifier(string identifier)
    {
        ReadOnlySpan<byte> identifierBytes = Encoding.UTF8.GetBytes(identifier);
        Span<byte> hashBuffer = stackalloc byte[32];
        SHA256.HashData(identifierBytes, hashBuffer);

        return Convert.ToBase64String(hashBuffer)
            .Replace('/', '_')
            .Replace('+', '-');
    }

    private Option<byte[]> GetMachineKey()
    {
        if (_cachedMachineKey != null)
        {
            // Return a clone to prevent callers from modifying the cached key
            return Option<byte[]>.Some((byte[])_cachedMachineKey.Clone());
        }

        bool keychainAvailable = IsPlatformKeychainAvailable();
        string machineId = BuildMachineIdentifier();

        // Try to get machine key from platform keychain first
        Result<byte[], SecureStorageFailure> keychainResult = GetPlatformRetrieve(MACHINE_KEY_IDENTIFIER);
        if (keychainResult.IsOk)
        {
            byte[] keychainKey = keychainResult.Unwrap();
            _cachedMachineKey = keychainKey;

            if (IsLegacyDerivedMachineKey(machineId, keychainKey))
            {
                TryUpgradeLegacyMachineKey(keychainKey, keychainAvailable);
            }
            return Option<byte[]>.Some((byte[])_cachedMachineKey.Clone());
        }

        // Fall back to file-based storage for migration
        string machineKeyFile = Path.Combine(_keychainPath, MACHINE_KEY_FILENAME);
        if (File.Exists(machineKeyFile))
        {
            Option<byte[]> fileResult = LoadMachineKeyFromFile(machineKeyFile, machineId, keychainAvailable);
            if (fileResult.IsSome)
            {
                // Migrate to platform keychain
                if (keychainAvailable)
                {
                    Result<Unit, SecureStorageFailure> storeResult = GetPlatformStore(MACHINE_KEY_IDENTIFIER, _cachedMachineKey!);
                    if (storeResult.IsOk)
                    {
                        // Successfully migrated - securely delete the file
                        TrySecureDeleteFile(machineKeyFile);
                        Log.Information("[MACHINE-KEY] Successfully migrated machine key to platform keychain");
                    }
                }
                return Option<byte[]>.Some((byte[])_cachedMachineKey!.Clone());
            }
        }

        return CreateAndStoreMachineKey(machineKeyFile, keychainAvailable);
    }

    private Option<byte[]> LoadMachineKeyFromFile(string machineKeyFile, string machineId, bool keychainAvailable)
    {
        try
        {
            byte[] fileKey = File.ReadAllBytes(machineKeyFile);

            if (!IsValidMachineKeySize(fileKey))
            {
                TrySecureDeleteFile(machineKeyFile);
                return Option<byte[]>.None;
            }

            _cachedMachineKey = fileKey;

            if (IsLegacyDerivedMachineKey(machineId, fileKey))
            {
                TryUpgradeLegacyMachineKey(fileKey, keychainAvailable);
            }

            // Return a clone to prevent callers from modifying the cached key
            return Option<byte[]>.Some((byte[])_cachedMachineKey.Clone());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MACHINE-KEY-ERROR] Failed to read existing machine key file: {Error}", ex.Message);
            _cachedMachineKey = null;
            return Option<byte[]>.None;
        }
    }

    private bool IsValidMachineKeySize(byte[] key)
    {
        if (key.Length == AES_KEY_SIZE)
        {
            return true;
        }

        Log.Error("[MACHINE-KEY] Invalid machine key size: {ActualSize}, expected: {ExpectedSize}",
            key.Length, AES_KEY_SIZE);
        _cachedMachineKey = null;
        return false;
    }

    private static bool IsLegacyDerivedMachineKey(string machineId, byte[] key)
    {
        // Use the OLD derivation method (with hardcoded salt only) to detect legacy keys
        byte[] derivedKey = DeriveKeyFromMachineIdLegacy(machineId);
        bool isLegacy = CryptographicOperations.FixedTimeEquals(derivedKey, key);
        CryptographicOperations.ZeroMemory(derivedKey);
        return isLegacy;
    }

    // Legacy derivation method - kept only for detecting/upgrading old keys
    // DO NOT use for new key derivation
    private static byte[] DeriveKeyFromMachineIdLegacy(string machineId)
    {
        byte[] salt = Encoding.UTF8.GetBytes(MACHINE_KEY_SALT);
        return Rfc2898DeriveBytes.Pbkdf2(
            machineId,
            salt,
            PBKDF_2_ITERATIONS,
            HashAlgorithmName.SHA256,
            AES_KEY_SIZE);
    }

    private void TryUpgradeLegacyMachineKey(byte[] legacyKey, bool keychainAvailable)
    {
        try
        {
            byte[] newKey = RandomNumberGenerator.GetBytes(AES_KEY_SIZE);
            bool storedInKeychain = false;
            bool persisted = false;
            string machineKeyFile = Path.Combine(_keychainPath, MACHINE_KEY_FILENAME);

            if (keychainAvailable)
            {
                Result<Unit, SecureStorageFailure> storeResult = GetPlatformStore(MACHINE_KEY_IDENTIFIER, newKey);
                if (storeResult.IsErr)
                {
                    Log.Warning("[MACHINE-KEY] Failed to store upgraded machine key in keychain: {Error}",
                        storeResult.UnwrapErr().Message);
                }
                else
                {
                    storedInKeychain = true;
                    persisted = true;
                }
            }

            if (!persisted)
            {
                persisted = WriteMachineKeyFile(machineKeyFile, newKey);
            }

            if (!persisted)
            {
                CryptographicOperations.ZeroMemory(newKey);
                return;
            }

            bool reencrypted = ReencryptKeyFiles(legacyKey, newKey);
            if (!reencrypted)
            {
                if (storedInKeychain)
                {
                    Result<Unit, SecureStorageFailure> revertResult =
                        GetPlatformStore(MACHINE_KEY_IDENTIFIER, legacyKey);
                    if (revertResult.IsErr)
                    {
                        Log.Warning("[MACHINE-KEY] Failed to revert machine key after upgrade failure: {Error}",
                            revertResult.UnwrapErr().Message);
                    }
                }
                else
                {
                    WriteMachineKeyFile(machineKeyFile, legacyKey);
                }

                CryptographicOperations.ZeroMemory(newKey);
                return;
            }
            else
            {
                TrySecureDeleteFile(machineKeyFile);
            }

            _cachedMachineKey = newKey;
            CryptographicOperations.ZeroMemory(legacyKey);
            Log.Information("[MACHINE-KEY] Upgraded legacy machine key");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MACHINE-KEY] Failed to upgrade legacy machine key");
        }
    }

    private bool ReencryptKeyFiles(byte[] legacyKey, byte[] newKey)
    {
        try
        {
            string[] keyFiles = Directory.GetFiles(_keychainPath, "*.key");
            if (keyFiles.Length == 0)
            {
                return true;
            }

            foreach (string keyFile in keyFiles)
            {
                if (string.Equals(Path.GetFileName(keyFile), MACHINE_KEY_FILENAME, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byte[] fileBytes = File.ReadAllBytes(keyFile);
                string hashedIdentifier = Path.GetFileNameWithoutExtension(keyFile);
                byte[] associatedData = BuildAssociatedDataFromHash(hashedIdentifier);

                try
                {
                    byte[] plaintext = DecryptKeyFile(fileBytes, legacyKey, associatedData, out _);
                    try
                    {
                        WriteEncryptedKeyFile(keyFile, plaintext, newKey, associatedData);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(associatedData);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MACHINE-KEY] Failed to re-encrypt key files during upgrade");
            return false;
        }
    }

    private static bool WriteMachineKeyFile(string machineKeyFile, byte[] key)
    {
        try
        {
            File.WriteAllBytes(machineKeyFile, key);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(machineKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MACHINE-KEY] Failed to persist machine key file: {Path}", machineKeyFile);
            return false;
        }
    }

    private Option<byte[]> CreateAndStoreMachineKey(string machineKeyFile, bool keychainAvailable)
    {
        _cachedMachineKey = RandomNumberGenerator.GetBytes(AES_KEY_SIZE);

        bool storedInKeychain = false;
        if (keychainAvailable)
        {
            Result<Unit, SecureStorageFailure> storeResult = GetPlatformStore(MACHINE_KEY_IDENTIFIER, _cachedMachineKey);
            if (storeResult.IsErr)
            {
                Log.Warning("[MACHINE-KEY] Platform keychain unavailable, falling back to file storage: {Error}",
                    storeResult.UnwrapErr().Message);
            }
            else
            {
                storedInKeychain = true;
            }
        }

        if (!storedInKeychain)
        {
            WriteMachineKeyFile(machineKeyFile, _cachedMachineKey);
        }

        // Return a clone to prevent callers from modifying the cached key
        return Option<byte[]>.Some((byte[])_cachedMachineKey.Clone());
    }

    private static void TryEnhanceWithHardwareRandom(Span<byte> bytes)
    {
        try
        {
            using FileStream hwRandom = File.OpenRead("/dev/random");
            int bytesRead = hwRandom.Read(bytes);
            if (bytesRead >= bytes.Length)
            {
                return;
            }

            Span<byte> tempBuffer = stackalloc byte[bytes.Length];
            RandomNumberGenerator.Fill(tempBuffer);
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= tempBuffer[i];
            }
        }
        catch (Exception)
        {
            // Hardware random enhancement is best-effort - fallback to software RNG is acceptable
        }
    }

    private static string BuildMachineIdentifier()
    {
        StringBuilder machineId = new(Environment.MachineName);
        machineId.Append(Environment.ProcessorCount);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            TryAppendLinuxMachineId(machineId);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            TryAppendMacOsuuid(machineId);
        }

        return machineId.ToString();
    }

    private static void TryAppendLinuxMachineId(StringBuilder machineId)
    {
        try
        {
            string id = File.ReadAllText(LINUX_MACHINE_ID_PATH).Trim();
            machineId.Append(id);
        }
        catch
        {
            machineId.Append("NoMachineId");
        }
    }

    private static void TryAppendMacOsuuid(StringBuilder machineId)
    {
        try
        {
            using System.Diagnostics.Process process = new();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ioreg",
                Arguments = "-rd1 -c IOPlatformExpertDevice",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
                machineId.Append("NoUUID-Timeout");
                return;
            }

            Match match = Regex.Match(output, MACOS_UUID_PATTERN, RegexOptions.None, TimeSpan.FromSeconds(1));
            machineId.Append(match.Success ? match.Groups[1].Value : "NoUUID");
        }
        catch
        {
            machineId.Append("NoIOReg");
        }
    }

    private byte[] DeriveKeyFromMachineId(string machineId)
    {
        // SECURITY FIX C5: Use random per-machine salt instead of hardcoded salt
        // This prevents rainbow table attacks and cross-machine key prediction
        byte[] randomSalt = GetOrCreateRandomMachineSalt();

        // Combine random salt with static context for domain separation
        byte[] combinedSalt = new byte[randomSalt.Length + 32];
        try
        {
            Array.Copy(randomSalt, 0, combinedSalt, 0, randomSalt.Length);
            byte[] contextHash = SHA256.HashData(Encoding.UTF8.GetBytes(MACHINE_KEY_SALT));
            Array.Copy(contextHash, 0, combinedSalt, randomSalt.Length, contextHash.Length);

            return Rfc2898DeriveBytes.Pbkdf2(
                machineId,
                combinedSalt,
                PBKDF_2_ITERATIONS,
                HashAlgorithmName.SHA256,
                AES_KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combinedSalt);
        }
    }

    private byte[] GetOrCreateRandomMachineSalt()
    {
        // Try to retrieve existing random salt from platform keychain
        Result<byte[], SecureStorageFailure> retrieveResult = GetPlatformRetrieve(MACHINE_SALT_IDENTIFIER);
        if (retrieveResult.IsOk)
        {
            byte[] existingSalt = retrieveResult.Unwrap();
            if (existingSalt.Length == RANDOM_SALT_SIZE)
            {
                return existingSalt;
            }
            // Invalid size - regenerate
            CryptographicOperations.ZeroMemory(existingSalt);
        }

        // Generate new random salt
        byte[] newSalt = RandomNumberGenerator.GetBytes(RANDOM_SALT_SIZE);

        // Store in platform keychain
        Result<Unit, SecureStorageFailure> storeResult = GetPlatformStore(MACHINE_SALT_IDENTIFIER, newSalt);
        if (storeResult.IsErr)
        {
            Log.Warning("[MACHINE-SALT] Failed to store random machine salt in keychain: {Error}",
                storeResult.UnwrapErr().Message);
            // Continue with the generated salt anyway - it will be regenerated on next app start
            // but this is still more secure than hardcoded salt
        }
        else
        {
            Log.Information("[MACHINE-SALT] Generated and stored new random machine salt");
        }

        return newSalt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    private static class MacOsKeychainErrors
    {
        public const int DuplicateItem = -25299;
        public const int ItemNotFound = -25300;
    }

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        string serviceName,
        uint accountNameLength,
        string accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        string serviceName,
        uint accountNameLength,
        string accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(IntPtr cf);

    private static class LinuxSecretService
    {
        private const string LibSecretLibrary = "libsecret-1.so.0";
        private const string LibGlibLibrary = "libglib-2.0.so.0";
        private const int SecretSchemaAttributeMax = 32;
        private const string SchemaName = "com.ecliptix.desktop";
        private const string AttributeName = "identifier";
        private const string SecretLabel = "Ecliptix Key Material";
        private static readonly Lazy<bool> Availability = new(Initialize);
        private static SecretSchema _schema;
        private static IntPtr _glibHandle;
        private static IntPtr _secretHandle;
        private static IntPtr _gStrHash;
        private static IntPtr _gStrEqual;
        private static IntPtr _gFree;
        private static int _resolverInitialized;

        internal static bool IsAvailable => Availability.Value;

        internal static Result<Unit, SecureStorageFailure> Store(string identifier, byte[] key)
        {
            if (!IsAvailable)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Linux secret service unavailable"));
            }

            IntPtr attributes = BuildAttributes(identifier);
            if (attributes == IntPtr.Zero)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to build secret attributes"));
            }

            try
            {
                string encoded = Convert.ToBase64String(key);
                bool ok = secret_password_storev_sync(ref _schema, null, SecretLabel, encoded,
                    attributes, IntPtr.Zero, out IntPtr error);

                if (!ok)
                {
                    string message = GetErrorMessage(error) ?? "Unknown error";
                    return Result<Unit, SecureStorageFailure>.Err(
                        new SecureStorageFailure($"Secret store failed: {message}"));
                }

                return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure($"Secret store failed: {ex.Message}"));
            }
            finally
            {
                g_hash_table_destroy(attributes);
            }
        }

        internal static Result<byte[], SecureStorageFailure> Get(string identifier)
        {
            if (!IsAvailable)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Linux secret service unavailable"));
            }

            IntPtr attributes = BuildAttributes(identifier);
            if (attributes == IntPtr.Zero)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to build secret attributes"));
            }

            IntPtr passwordPtr = IntPtr.Zero;
            try
            {
                passwordPtr = secret_password_lookupv_sync(ref _schema, attributes, IntPtr.Zero, out IntPtr error);
                if (passwordPtr == IntPtr.Zero)
                {
                    string message = GetErrorMessage(error) ?? "Secret not found";
                    return Result<byte[], SecureStorageFailure>.Err(
                        new SecureStorageFailure($"Secret lookup failed: {message}"));
                }

                string? encoded = Marshal.PtrToStringUTF8(passwordPtr);
                if (string.IsNullOrEmpty(encoded))
                {
                    return Result<byte[], SecureStorageFailure>.Err(
                        new SecureStorageFailure("Secret lookup returned empty value"));
                }

                try
                {
                    byte[] key = Convert.FromBase64String(encoded);
                    return Result<byte[], SecureStorageFailure>.Ok(key);
                }
                catch (FormatException ex)
                {
                    return Result<byte[], SecureStorageFailure>.Err(
                        new SecureStorageFailure($"Secret value invalid: {ex.Message}"));
                }
            }
            catch (Exception ex)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure($"Secret lookup failed: {ex.Message}"));
            }
            finally
            {
                if (passwordPtr != IntPtr.Zero)
                {
                    secret_password_free(passwordPtr);
                }
                g_hash_table_destroy(attributes);
            }
        }

        internal static Result<Unit, SecureStorageFailure> Delete(string identifier)
        {
            if (!IsAvailable)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Linux secret service unavailable"));
            }

            IntPtr attributes = BuildAttributes(identifier);
            if (attributes == IntPtr.Zero)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to build secret attributes"));
            }

            try
            {
                bool ok = secret_password_clearv_sync(ref _schema, attributes, IntPtr.Zero, out IntPtr error);
                if (!ok)
                {
                    string message = GetErrorMessage(error) ?? "Secret not found";
                    if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
                    }

                    return Result<Unit, SecureStorageFailure>.Err(
                        new SecureStorageFailure($"Secret delete failed: {message}"));
                }

                return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure($"Secret delete failed: {ex.Message}"));
            }
            finally
            {
                g_hash_table_destroy(attributes);
            }
        }

        private static bool Initialize()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return false;
            }

            _secretHandle = TryLoadLibrary(new[] { LibSecretLibrary, "libsecret-1.so", "libsecret.so.1" });
            if (_secretHandle == IntPtr.Zero)
            {
                return false;
            }

            _glibHandle = TryLoadLibrary(new[] { LibGlibLibrary, "libglib-2.0.so", "libglib.so.0" });
            if (_glibHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                EnsureDllImportResolver();
                _gStrHash = NativeLibrary.GetExport(_glibHandle, "g_str_hash");
                _gStrEqual = NativeLibrary.GetExport(_glibHandle, "g_str_equal");
                _gFree = NativeLibrary.GetExport(_glibHandle, "g_free");
                _schema = BuildSchema();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr TryLoadLibrary(string[] names)
        {
            foreach (string name in names)
            {
                if (NativeLibrary.TryLoad(name, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        private static void EnsureDllImportResolver()
        {
            if (Interlocked.Exchange(ref _resolverInitialized, 1) == 1)
            {
                return;
            }

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(CrossPlatformSecurityProvider).Assembly, ResolveImport);
            }
            catch (InvalidOperationException)
            {
                // Resolver already set for this assembly.
            }
        }

        private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == LibSecretLibrary && _secretHandle != IntPtr.Zero)
            {
                return _secretHandle;
            }

            if (libraryName == LibGlibLibrary && _glibHandle != IntPtr.Zero)
            {
                return _glibHandle;
            }

            return IntPtr.Zero;
        }

        private static SecretSchema BuildSchema()
        {
            SecretSchema schema = new()
            {
                Name = Marshal.StringToHGlobalAnsi(SchemaName),
                Flags = SecretSchemaFlags.None,
                Attributes = new SecretSchemaAttribute[SecretSchemaAttributeMax]
            };

            schema.Attributes[0] = new SecretSchemaAttribute
            {
                Name = Marshal.StringToHGlobalAnsi(AttributeName),
                Type = SecretSchemaAttributeType.String
            };

            schema.Attributes[1] = new SecretSchemaAttribute();
            return schema;
        }

        private static IntPtr BuildAttributes(string identifier)
        {
            IntPtr table = g_hash_table_new_full(_gStrHash, _gStrEqual, _gFree, _gFree);
            if (table == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr key = g_strdup(AttributeName);
            IntPtr value = g_strdup(identifier);
            g_hash_table_insert(table, key, value);
            return table;
        }

        private static string? GetErrorMessage(IntPtr error)
        {
            if (error == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                GError gerror = Marshal.PtrToStructure<GError>(error);
                return Marshal.PtrToStringUTF8(gerror.Message);
            }
            finally
            {
                g_error_free(error);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecretSchemaAttribute
        {
            public IntPtr Name;
            public SecretSchemaAttributeType Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecretSchema
        {
            public IntPtr Name;
            public SecretSchemaFlags Flags;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = SecretSchemaAttributeMax)]
            public SecretSchemaAttribute[] Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GError
        {
            public uint Domain;
            public int Code;
            public IntPtr Message;
        }

        private enum SecretSchemaAttributeType
        {
            String = 0
        }

        [Flags]
        private enum SecretSchemaFlags
        {
            None = 0
        }

        [DllImport(LibSecretLibrary, EntryPoint = "secret_password_storev_sync")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool secret_password_storev_sync(
            ref SecretSchema schema,
            string? collection,
            string label,
            string password,
            IntPtr attributes,
            IntPtr cancellable,
            out IntPtr error);

        [DllImport(LibSecretLibrary, EntryPoint = "secret_password_lookupv_sync")]
        private static extern IntPtr secret_password_lookupv_sync(
            ref SecretSchema schema,
            IntPtr attributes,
            IntPtr cancellable,
            out IntPtr error);

        [DllImport(LibSecretLibrary, EntryPoint = "secret_password_clearv_sync")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool secret_password_clearv_sync(
            ref SecretSchema schema,
            IntPtr attributes,
            IntPtr cancellable,
            out IntPtr error);

        [DllImport(LibSecretLibrary, EntryPoint = "secret_password_free")]
        private static extern void secret_password_free(IntPtr password);

        [DllImport(LibGlibLibrary)]
        private static extern IntPtr g_hash_table_new_full(
            IntPtr hashFunc,
            IntPtr keyEqualFunc,
            IntPtr keyDestroyFunc,
            IntPtr valueDestroyFunc);

        [DllImport(LibGlibLibrary)]
        private static extern void g_hash_table_insert(IntPtr hashTable, IntPtr key, IntPtr value);

        [DllImport(LibGlibLibrary)]
        private static extern void g_hash_table_destroy(IntPtr hashTable);

        [DllImport(LibGlibLibrary)]
        private static extern IntPtr g_strdup(string str);

        [DllImport(LibGlibLibrary)]
        private static extern void g_error_free(IntPtr error);
    }

    private static bool CheckLinuxTpm() =>
        File.Exists(LINUX_TPM_PATH) || File.Exists(LINUX_TPMRM_PATH);

    private static bool CheckWindowsTpm()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(TPM_REGISTRY_PATH);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckMacOsSecureEnclave()
    {
        try
        {
            using System.Diagnostics.Process process = new();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sysctl",
                Arguments = "-n hw.optional.arm64",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return false;
            }

            return output.Trim() == "1";
        }
        catch
        {
            return false;
        }
    }

    // SECURITY FIX C6: Implement IDisposable with proper key zeroing
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        // Zero sensitive key material regardless of disposing flag
        // This ensures keys are cleared even during finalization
        lock (_lockObject)
        {
            if (_cachedMachineKey != null)
            {
                CryptographicOperations.ZeroMemory(_cachedMachineKey);
                _cachedMachineKey = null;
                Log.Debug("[SECURITY-DISPOSE] Machine key zeroed and cleared");
            }

            if (_cachedHmacKey != null)
            {
                CryptographicOperations.ZeroMemory(_cachedHmacKey);
                _cachedHmacKey = null;
                Log.Debug("[SECURITY-DISPOSE] HMAC key zeroed and cleared");
            }
        }

        _disposed = true;
    }

    ~CrossPlatformSecurityProvider()
    {
        Dispose(disposing: false);
    }
}
