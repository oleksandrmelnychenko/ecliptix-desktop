using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
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
    private const int HMAC_KEY_SIZE = 64;
    private const int PBKDF_2_ITERATIONS = 100000;
    private const int SECURE_OVERWRITE_SIZE = 1024;
    private const string MACHINE_KEY_SALT = "EcliptixMachineKey";
    private const string HMAC_KEY_IDENTIFIER = "ecliptix_hmac_key";
    private const string MACHINE_KEY_FILENAME = ".machine.key";
    private const string KEYCHAIN_FOLDER = ".keychain";
    private const string TPM_REGISTRY_PATH = @"SYSTEM\CurrentControlSet\Services\TPM\";
    private const string LINUX_MACHINE_ID_PATH = "/etc/machine-id";
    private const string LINUX_TPM_PATH = "/dev/tpm0";
    private const string LINUX_TPMRM_PATH = "/dev/tpmrm0";
    private const string MACOS_UUID_PATTERN = @"IOPlatformUUID""\s*=\s*""([^""]+)""";

    private readonly string _keychainPath;
    private readonly Lock _lockObject = new();

    private byte[]? _cachedMachineKey;
    private byte[]? _cachedHmacKey;

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
                    return fileResult.IsOk ? fileResult.Unwrap() : null;
                }
                catch (Exception)
                {
                    Result<byte[], SecureStorageFailure> fileResult = GetFromEncryptedFile(identifier);
                    return fileResult.IsOk ? fileResult.Unwrap() : null;
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

    public async Task DeleteKeyFromKeychainAsync(string identifier)
    {
        await Task.Run(() =>
        {
            lock (_lockObject)
            {
                string keyFile = GetKeyFilePath(identifier);
                try
                {
                    if (!File.Exists(keyFile))
                    {
                        return;
                    }


                    byte[] randomData = RandomNumberGenerator.GetBytes(SECURE_OVERWRITE_SIZE);

                    using (FileStream fs = File.OpenWrite(keyFile))
                    {
                        fs.Write(randomData, 0, randomData.Length);
                        fs.Flush(true);
                    }

                    File.Delete(keyFile);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[KEYCHAIN-CLEANUP] Could not delete old key file during rotation: {KeyFile}",
                        keyFile);
                }
            }
        });
    }

    public async Task<byte[]> GetOrCreateHmacKeyAsync()
    {
        if (_cachedHmacKey != null)
        {
            return _cachedHmacKey;
        }

        byte[]? hmacKey = await GetKeyFromKeychainAsync(HMAC_KEY_IDENTIFIER);

        if (hmacKey == null)
        {
            byte[] newKey = await GenerateSecureRandomAsync(HMAC_KEY_SIZE);
            await StoreKeyInKeychainAsync(HMAC_KEY_IDENTIFIER, newKey);

            _cachedHmacKey = newKey;
            return newKey;
        }

        _cachedHmacKey = hmacKey;
        return hmacKey;
    }

    public bool IsHardwareSecurityAvailable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CheckWindowsTpm();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return CheckMacOsSecureEnclave();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return CheckLinuxTpm();
        }

        return false;
    }

    public async Task<Option<byte[]>> HardwareEncryptAsync(byte[] data)
    {
        byte[] key = await GetOrCreateHmacKeyAsync();

        try
        {
            using Aes aes = Aes.Create();
            Span<byte> keySpan = key.AsSpan(0, AES_KEY_SIZE);
            aes.Key = keySpan.ToArray();
            aes.GenerateIV();

            using MemoryStream ms = new();
            await ms.WriteAsync(aes.IV.AsMemory(0, AES_IV_SIZE));

            await using (CryptoStream cryptoStream = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                await cryptoStream.WriteAsync(data);
                await cryptoStream.FlushFinalBlockAsync();
            }

            return Option<byte[]>.Some(ms.ToArray());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware encryption failed");
            return Option<byte[]>.None;
        }
    }

    public async Task<Option<byte[]>> HardwareDecryptAsync(byte[] data)
    {
        if (data.Length < AES_IV_SIZE)
        {
            Log.Error("[HARDWARE-DECRYPT-ERROR] Invalid encrypted data format - data too small. Length: {Length}, MinSize: {MinSize}",
                data.Length, AES_IV_SIZE);
            return Option<byte[]>.None;
        }

        byte[] key = await GetOrCreateHmacKeyAsync();

        try
        {
            using Aes aes = Aes.Create();
            Span<byte> keySpan = key.AsSpan(0, AES_KEY_SIZE);
            aes.Key = keySpan.ToArray();

            byte[] iv = new byte[AES_IV_SIZE];
            Array.Copy(data, 0, iv, 0, AES_IV_SIZE);
            aes.IV = iv;

            using MemoryStream ms = new(data, AES_IV_SIZE, data.Length - AES_IV_SIZE);
            await using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using MemoryStream result = new();

            await cs.CopyToAsync(result);
            return Option<byte[]>.Some(result.ToArray());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HARDWARE-DECRYPT-ERROR] Hardware decryption failed: {ERROR}", ex.Message);
            return Option<byte[]>.None;
        }
    }

    private Result<Unit, SecureStorageFailure> StoreInWindowsCredentialManager(string identifier, byte[] key) =>
        StoreInEncryptedFile(identifier, key);

    private Result<byte[], SecureStorageFailure> GetFromWindowsCredentialManager(string identifier) =>
        GetFromEncryptedFile(identifier);

    private Result<Unit, SecureStorageFailure> StoreInMacOsKeychain(string identifier, byte[] key) =>
        StoreInEncryptedFile(identifier, key);

    private Result<byte[], SecureStorageFailure> GetFromMacOsKeychain(string identifier) =>
        GetFromEncryptedFile(identifier);

    private Result<Unit, SecureStorageFailure> StoreInLinuxSecretService(string identifier, byte[] key) =>
        StoreInEncryptedFile(identifier, key);

    private Result<byte[], SecureStorageFailure> GetFromLinuxSecretService(string identifier) =>
        GetFromEncryptedFile(identifier);

    private Result<Unit, SecureStorageFailure> StoreInEncryptedFile(string identifier, byte[] key)
    {
        try
        {
            string keyFile = GetKeyFilePath(identifier);

            Option<byte[]> machineKeyOpt = GetMachineKey();
            if (!machineKeyOpt.IsSome)
            {
                return Result<Unit, SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to get machine key"));
            }
            byte[] machineKey = machineKeyOpt.Value!;

            using Aes aes = Aes.Create();
            aes.Key = machineKey;
            aes.GenerateIV();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(AES_IV_SIZE + key.Length + 16);
            try
            {
                aes.IV.CopyTo(buffer, 0);

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                int encryptedLength = encryptor.TransformBlock(key, 0, key.Length, buffer, AES_IV_SIZE);
                byte[] finalBlock = encryptor.TransformFinalBlock(key, 0, 0);

                if (finalBlock.Length > 0)
                {
                    finalBlock.CopyTo(buffer, AES_IV_SIZE + encryptedLength);
                    encryptedLength += finalBlock.Length;
                }

                using FileStream fs = File.Create(keyFile);
                fs.Write(buffer, 0, AES_IV_SIZE + encryptedLength);
                fs.Flush(true);

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
        catch (Exception ex)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Failed to store encrypted file: {ex.Message}"));
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

        try
        {
            byte[] encrypted = File.ReadAllBytes(keyFile);
            if (encrypted.Length < AES_IV_SIZE)
            {
                Log.Error(
                    "[KEYCHAIN-FILE-ERROR] Invalid encrypted file format (too small). Identifier: {Identifier}, Size: {Size}, MinSize: {MinSize}",
                    identifier, encrypted.Length, AES_IV_SIZE);
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Invalid encrypted file format"));
            }

            Option<byte[]> machineKeyOpt = GetMachineKey();
            if (!machineKeyOpt.IsSome)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Failed to get machine key"));
            }
            byte[] machineKey = machineKeyOpt.Value!;

            using Aes aes = Aes.Create();
            aes.Key = machineKey;

            Span<byte> iv = encrypted.AsSpan(0, AES_IV_SIZE);
            aes.IV = iv.ToArray();

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(encrypted, AES_IV_SIZE, encrypted.Length - AES_IV_SIZE);

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
                    "[KEYCHAIN-FILE-DELETE-ERROR] Failed to delete corrupted key file. Identifier: {Identifier}, Path: {Path}, ERROR: {ERROR}",
                    identifier, keyFile, deleteEx.Message);
            }

            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure("Decryption failed - corrupted or wrong key"));
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "[KEYCHAIN-FILE-ERROR] Failed to read encrypted file. Identifier: {Identifier}, Path: {Path}, ERROR: {ERROR}",
                identifier, keyFile, ex.Message);
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure($"Failed to read encrypted file: {ex.Message}"));
        }
    }

    private string GetKeyFilePath(string identifier)
    {
        ReadOnlySpan<byte> identifierBytes = Encoding.UTF8.GetBytes(identifier);
        Span<byte> hashBuffer = stackalloc byte[32];
        SHA256.HashData(identifierBytes, hashBuffer);

        string safeIdentifier = Convert.ToBase64String(hashBuffer)
            .Replace('/', '_')
            .Replace('+', '-');

        return Path.Combine(_keychainPath, $"{safeIdentifier}.key");
    }

    private Option<byte[]> GetMachineKey()
    {
        if (_cachedMachineKey != null)
        {
            return Option<byte[]>.Some(_cachedMachineKey);
        }

        string machineKeyFile = Path.Combine(_keychainPath, MACHINE_KEY_FILENAME);
        string machineId = BuildMachineIdentifier();

        if (File.Exists(machineKeyFile))
        {
            try
            {
                _cachedMachineKey = File.ReadAllBytes(machineKeyFile);

                if (_cachedMachineKey.Length != AES_KEY_SIZE)
                {
                    Log.Error("[MACHINE-KEY] Invalid machine key size: {ActualSize}, expected: {ExpectedSize}",
                        _cachedMachineKey.Length, AES_KEY_SIZE);
                    _cachedMachineKey = null;
                    return Option<byte[]>.None;
                }

                byte[] derivedKey = DeriveKeyFromMachineId(machineId);

                if (!CryptographicOperations.FixedTimeEquals(derivedKey, _cachedMachineKey))
                {
                    Log.Warning("[MACHINE-KEY] Machine key verification failed - regenerating from current machine ID");
                    CryptographicOperations.ZeroMemory(derivedKey);
                    _cachedMachineKey = null;

                    try
                    {
                        File.Delete(machineKeyFile);

                        try
                        {
                            string[] existingKeyFiles = Directory.GetFiles(_keychainPath, "*.key");
                            if (existingKeyFiles.Length > 0)
                            {
                                Log.Warning("[MACHINE-KEY] Deleting {Count} obsolete encrypted key files", existingKeyFiles.Length);
                                foreach (string keyFile in existingKeyFiles)
                                {
                                    try
                                    {
                                        File.Delete(keyFile);
                                    }
                                    catch (Exception fileEx)
                                    {
                                        Log.Warning(fileEx, "[MACHINE-KEY] Failed to delete obsolete key file: {File}", Path.GetFileName(keyFile));
                                    }
                                }
                            }
                        }
                        catch (Exception cleanupEx)
                        {
                            Log.Warning(cleanupEx, "[MACHINE-KEY] Failed to cleanup obsolete key files: {ERROR}", cleanupEx.Message);
                        }

                        _cachedMachineKey = DeriveKeyFromMachineId(machineId);

                        try
                        {
                            File.WriteAllBytes(machineKeyFile, _cachedMachineKey);
                            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                File.SetUnixFileMode(machineKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                            }
                        }
                        catch (Exception writeEx)
                        {
                            Log.Error(writeEx, "[MACHINE-KEY-ERROR] Failed to write regenerated machine key file: {ERROR}", writeEx.Message);
                        }

                        return Option<byte[]>.Some(_cachedMachineKey);
                    }
                    catch (Exception deleteEx)
                    {
                        Log.Error(deleteEx, "[MACHINE-KEY-ERROR] Failed to delete corrupted machine key file: {ERROR}", deleteEx.Message);
                        return Option<byte[]>.None;
                    }
                }

                CryptographicOperations.ZeroMemory(derivedKey);
                return Option<byte[]>.Some(_cachedMachineKey);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MACHINE-KEY-ERROR] Failed to read existing machine key file: {ERROR}",
                    ex.Message);
                _cachedMachineKey = null;
                return Option<byte[]>.None;
            }
        }

        _cachedMachineKey = DeriveKeyFromMachineId(machineId);

        try
        {
            File.WriteAllBytes(machineKeyFile, _cachedMachineKey);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(machineKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MACHINE-KEY-ERROR] Failed to write machine key file: {ERROR}",
                ex.Message);
        }

        return Option<byte[]>.Some(_cachedMachineKey);
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
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ioreg",
                    Arguments = "-rd1 -c IOPlatformExpertDevice",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
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

    private static byte[] DeriveKeyFromMachineId(string machineId)
    {
        ReadOnlySpan<byte> salt = SHA256.HashData(Encoding.UTF8.GetBytes(MACHINE_KEY_SALT));

        using Rfc2898DeriveBytes pbkdf2 = new(
            machineId,
            salt.ToArray(),
            PBKDF_2_ITERATIONS,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(AES_KEY_SIZE);
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
}
