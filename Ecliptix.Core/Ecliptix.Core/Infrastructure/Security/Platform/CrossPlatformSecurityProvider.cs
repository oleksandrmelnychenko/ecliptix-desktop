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
using Serilog.Events;

namespace Ecliptix.Core.Infrastructure.Security.Platform;

internal sealed class CrossPlatformSecurityProvider : IPlatformSecurityProvider
{
    private const int AesKeySize = 32;
    private const int AesIvSize = 16;
    private const int HmacKeySize = 64;
    private const int Pbkdf2Iterations = 100000;
    private const int SecureOverwriteSize = 1024;
    private const string MachineKeySalt = "EcliptixMachineKey";
    private const string HmacKeyIdentifier = "ecliptix_hmac_key";
    private const string MachineKeyFilename = ".machine.key";
    private const string KeychainFolder = ".keychain";
    private const string TpmRegistryPath = @"SYSTEM\CurrentControlSet\Services\TPM\";
    private const string LinuxMachineIdPath = "/etc/machine-id";
    private const string LinuxTpmPath = "/dev/tpm0";
    private const string LinuxTpmrmPath = "/dev/tpmrm0";
    private const string MacosUuidPattern = @"IOPlatformUUID""\s*=\s*""([^""]+)""";

    private readonly string _keychainPath;
    private readonly Lock _lockObject = new();

    private byte[]? _cachedMachineKey;
    private byte[]? _cachedHmacKey;

    public CrossPlatformSecurityProvider(string appDataPath)
    {
        _keychainPath = Path.Combine(appDataPath, KeychainFolder);
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
                        StoreInEncryptedFile(identifier, key);
                    }
                }
                catch (Exception)
                {
                    StoreInEncryptedFile(identifier, key);
                }
            }
        });
    }

    private Result<Unit, SecureStorageFailure> GetPlatformStore(string identifier, byte[] key) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StoreInWindowsCredentialManager(identifier, key) :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? StoreInMacOsKeychain(identifier, key) :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StoreInLinuxSecretService(identifier, key) :
        StoreInEncryptedFile(identifier, key);

    public async Task<byte[]?> GetKeyFromKeychainAsync(string identifier)
    {
        return await Task.Run(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    Result<byte[], SecureStorageFailure> result = GetPlatformRetrieve(identifier);
                    if (result.IsErr)
                    {
                        Result<byte[], SecureStorageFailure> fileResult = GetFromEncryptedFile(identifier);
                        return fileResult.IsOk ? fileResult.Unwrap() : null;
                    }

                    return result.IsOk ? result.Unwrap() : null;
                }
                catch (Exception)
                {
                    Result<byte[], SecureStorageFailure> fileResult = GetFromEncryptedFile(identifier);
                    return fileResult.IsOk ? fileResult.Unwrap() : null;
                }
            }
        });
    }

    private Result<byte[], SecureStorageFailure> GetPlatformRetrieve(string identifier) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetFromWindowsCredentialManager(identifier) :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? GetFromMacOsKeychain(identifier) :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetFromLinuxSecretService(identifier) :
        GetFromEncryptedFile(identifier);

    public async Task DeleteKeyFromKeychainAsync(string identifier)
    {
        await Task.Run(() =>
        {
            lock (_lockObject)
            {
                try
                {
                    string keyFile = GetKeyFilePath(identifier);
                    if (!File.Exists(keyFile))
                    {
                        return;
                    }


                    byte[] randomData = RandomNumberGenerator.GetBytes(SecureOverwriteSize);

                    using (FileStream fs = File.OpenWrite(keyFile))
                    {
                        fs.Write(randomData, 0, randomData.Length);
                        fs.Flush(true);
                    }

                    File.Delete(keyFile);
                }
                catch (Exception)
                {
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

        byte[]? hmacKey = await GetKeyFromKeychainAsync(HmacKeyIdentifier);

        if (hmacKey == null)
        {
            byte[] newKey = await GenerateSecureRandomAsync(HmacKeySize);
            await StoreKeyInKeychainAsync(HmacKeyIdentifier, newKey);

            _cachedHmacKey = newKey;
            return newKey;
        }

        _cachedHmacKey = hmacKey;
        return hmacKey;
    }

    public bool IsHardwareSecurityAvailable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? CheckWindowsTpm() :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? CheckMacOsSecureEnclave() :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && CheckLinuxTpm();

    public async Task<byte[]> HardwareEncryptAsync(byte[] data)
    {
        byte[] key = await GetOrCreateHmacKeyAsync();

        try
        {
            using Aes aes = Aes.Create();
            Span<byte> keySpan = key.AsSpan(0, AesKeySize);
            aes.Key = keySpan.ToArray();
            aes.GenerateIV();

            using MemoryStream ms = new();
            ms.Write(aes.IV, 0, AesIvSize);

            await using (CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(data, 0, data.Length);
                await cs.FlushFinalBlockAsync();
            }

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware encryption failed");
            throw;
        }
    }

    public async Task<byte[]> HardwareDecryptAsync(byte[] data)
    {
        if (data.Length < AesIvSize)
        {
            throw new ArgumentException("Invalid encrypted data format");
        }

        byte[] key = await GetOrCreateHmacKeyAsync();

        try
        {
            using Aes aes = Aes.Create();
            Span<byte> keySpan = key.AsSpan(0, AesKeySize);
            aes.Key = keySpan.ToArray();

            byte[] iv = new byte[AesIvSize];
            Array.Copy(data, 0, iv, 0, AesIvSize);
            aes.IV = iv;

            using MemoryStream ms = new(data, AesIvSize, data.Length - AesIvSize);
            await using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using MemoryStream result = new();

            await cs.CopyToAsync(result);
            return result.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hardware decryption failed");
            throw;
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
            byte[] machineKey = GetMachineKey();

            using Aes aes = Aes.Create();
            aes.Key = machineKey;
            aes.GenerateIV();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(AesIvSize + key.Length + 16);
            try
            {
                aes.IV.CopyTo(buffer, 0);

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                int encryptedLength = encryptor.TransformBlock(key, 0, key.Length, buffer, AesIvSize);
                byte[] finalBlock = encryptor.TransformFinalBlock(key, 0, 0);

                if (finalBlock.Length > 0)
                {
                    finalBlock.CopyTo(buffer, AesIvSize + encryptedLength);
                    encryptedLength += finalBlock.Length;
                }

                using FileStream fs = File.Create(keyFile);
                fs.Write(buffer, 0, AesIvSize + encryptedLength);

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
            Log.Debug("[KEYCHAIN-FILE] Key file not found. Identifier: {Identifier}, Path: {Path}",
                identifier, keyFile);
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure("Key file not found"));
        }

        FileInfo fileInfo = new(keyFile);

        try
        {
            byte[] encrypted = File.ReadAllBytes(keyFile);
            if (encrypted.Length < AesIvSize)
            {
                Log.Error("[KEYCHAIN-FILE-ERROR] Invalid encrypted file format (too small). Identifier: {Identifier}, Size: {Size}, MinSize: {MinSize}",
                    identifier, encrypted.Length, AesIvSize);
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Invalid encrypted file format"));
            }

            byte[] machineKey = GetMachineKey();

            using Aes aes = Aes.Create();
            aes.Key = machineKey;

            Span<byte> iv = encrypted.AsSpan(0, AesIvSize);
            aes.IV = iv.ToArray();

            if (Serilog.Log.IsEnabled(LogEventLevel.Debug))
            {
                string machineKeyFingerprint = Convert.ToHexString(SHA256.HashData(machineKey))[..16];
                Log.Debug("[KEYCHAIN-FILE] Decrypting with machine key. Identifier: {Identifier}, MachineKeyFingerprint: {MachineKeyFingerprint}, EncryptedSize: {Size}",
                    identifier, machineKeyFingerprint, encrypted.Length);
            }

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(encrypted, AesIvSize, encrypted.Length - AesIvSize);

            return Result<byte[], SecureStorageFailure>.Ok(decrypted);
        }
        catch (CryptographicException cryptoEx)
        {
            byte[] machineKey = GetMachineKey();
            string machineKeyFingerprint = Convert.ToHexString(SHA256.HashData(machineKey))[..16];

            Log.Error("[KEYCHAIN-FILE-DECRYPT-FAILED] Decryption failed - likely machine key mismatch or file corruption. Identifier: {Identifier}, Path: {Path}, Size: {Size}, MachineKeyFingerprint: {MachineKeyFingerprint}, Error: {Error}, LastModified: {LastModified}",
                identifier, keyFile, fileInfo.Length, machineKeyFingerprint, cryptoEx.Message, fileInfo.LastWriteTimeUtc);

            try
            {
                File.Delete(keyFile);
                Log.Warning("[KEYCHAIN-FILE-DELETED] Corrupted key file deleted. Identifier: {Identifier}, Path: {Path}",
                    identifier, keyFile);
            }
            catch (Exception deleteEx)
            {
                Log.Error("[KEYCHAIN-FILE-DELETE-ERROR] Failed to delete corrupted key file. Identifier: {Identifier}, Path: {Path}, Error: {Error}",
                    identifier, keyFile, deleteEx.Message);
            }

            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure("Decryption failed - corrupted or wrong key"));
        }
        catch (Exception ex)
        {
            Log.Error("[KEYCHAIN-FILE-ERROR] Failed to read encrypted file. Identifier: {Identifier}, Path: {Path}, Error: {Error}",
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

    private byte[] GetMachineKey()
    {
        if (_cachedMachineKey != null)
        {
            Log.Debug("[MACHINE-KEY] Using cached machine key");
            return _cachedMachineKey;
        }

        string machineKeyFile = Path.Combine(_keychainPath, MachineKeyFilename);
        string machineId = BuildMachineIdentifier();

        if (File.Exists(machineKeyFile))
        {
            try
            {
                _cachedMachineKey = File.ReadAllBytes(machineKeyFile);
                if (_cachedMachineKey.Length == AesKeySize)
                {
                    string storedKeyFingerprint = Convert.ToHexString(SHA256.HashData(_cachedMachineKey))[..16];

                    byte[] derivedKey = DeriveKeyFromMachineId(machineId);
                    string derivedKeyFingerprint = Convert.ToHexString(SHA256.HashData(derivedKey))[..16];

                    if (!derivedKey.AsSpan().SequenceEqual(_cachedMachineKey))
                    {
                        Log.Warning("[MACHINE-KEY-MISMATCH] Machine identifier changed! StoredKey: {StoredKey}, DerivedKey: {DerivedKey}. This will cause decryption failures for all encrypted files!",
                            storedKeyFingerprint, derivedKeyFingerprint);
                    }

                    CryptographicOperations.ZeroMemory(derivedKey);
                    return _cachedMachineKey;
                }
                else
                {
                    Log.Warning("[MACHINE-KEY] Existing machine key file has invalid size: {Size} bytes (expected {Expected})",
                        _cachedMachineKey.Length, AesKeySize);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MACHINE-KEY-ERROR] Failed to read existing machine key file: {Error}",
                    ex.Message);
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
            Log.Error("[MACHINE-KEY-ERROR] Failed to write machine key file: {Error}",
                ex.Message);
        }

        return _cachedMachineKey;
    }

    private static void TryEnhanceWithHardwareRandom(Span<byte> bytes)
    {
        try
        {
            using FileStream hwRandom = File.OpenRead("/dev/random");
            int bytesRead = hwRandom.Read(bytes);
            if (bytesRead < bytes.Length)
            {
                Span<byte> tempBuffer = stackalloc byte[bytes.Length];
                RandomNumberGenerator.Fill(tempBuffer);
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] ^= tempBuffer[i];
                }
            }
        }
        catch
        {
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
            string id = File.ReadAllText(LinuxMachineIdPath).Trim();
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
            process.WaitForExit();

            Match match = Regex.Match(output, MacosUuidPattern);
            machineId.Append(match.Success ? match.Groups[1].Value : "NoUUID");
        }
        catch
        {
            machineId.Append("NoIOReg");
        }
    }

    private static byte[] DeriveKeyFromMachineId(string machineId)
    {
        ReadOnlySpan<byte> salt = SHA256.HashData(Encoding.UTF8.GetBytes(MachineKeySalt));

        using Rfc2898DeriveBytes pbkdf2 = new(
            machineId,
            salt.ToArray(),
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(AesKeySize);
    }

    private static bool CheckLinuxTpm() =>
        File.Exists(LinuxTpmPath) || File.Exists(LinuxTpmrmPath);

    private static bool CheckWindowsTpm()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(TpmRegistryPath);
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
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n hw.optional.arm64",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Trim() == "1";
        }
        catch
        {
            return false;
        }
    }
}
