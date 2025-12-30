using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string basePath = Path.Combine(Path.GetTempPath(), "ecliptix-keychain-smoke");
        Directory.CreateDirectory(basePath);

        string identifier = $"smoke-{Guid.NewGuid():N}";
        string hashedIdentifier = HashIdentifier(identifier);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        bool keepEntry = Array.Exists(args, arg => arg.Equals("--keep", StringComparison.OrdinalIgnoreCase));

        IPlatformSecurityProvider provider = CreateProvider(basePath);
        Console.WriteLine($"Platform: {GetPlatformName()}");
        Console.WriteLine($"Keychain available: {provider.IsHardwareSecurityAvailable()}");
        Console.WriteLine($"Identifier: {identifier}");
        Console.WriteLine($"Hashed identifier: {hashedIdentifier}");
        PrintLookupHints(hashedIdentifier);

        await provider.StoreKeyInKeychainAsync(identifier, key);
        byte[]? fetched = await provider.GetKeyFromKeychainAsync(identifier);
        bool match = fetched != null && CryptographicOperations.FixedTimeEquals(key, fetched);
        Console.WriteLine($"Store+Get: {(match ? "OK" : "FAIL")}");

        if (fetched != null)
        {
            CryptographicOperations.ZeroMemory(fetched);
        }

        byte[]? afterDelete = null;
        if (!keepEntry)
        {
            await provider.DeleteKeyFromKeychainAsync(identifier);
            afterDelete = await provider.GetKeyFromKeychainAsync(identifier);
            Console.WriteLine($"Delete+Get: {(afterDelete == null ? "OK" : "FAIL")}");

            if (afterDelete != null)
            {
                CryptographicOperations.ZeroMemory(afterDelete);
            }
        }
        else
        {
            Console.WriteLine("Delete+Get: SKIPPED (--keep)");
        }

        CryptographicOperations.ZeroMemory(key);
        if (!keepEntry)
        {
            TryDeleteDirectory(basePath);
        }

        return match && (keepEntry || afterDelete == null) ? 0 : 1;
    }

    private static IPlatformSecurityProvider CreateProvider(string appDataPath)
    {
        const string typeName =
            "Ecliptix.Core.Infrastructure.Security.Platform.CrossPlatformSecurityProvider, Ecliptix.Core";
        Type type = Type.GetType(typeName, throwOnError: true)!;
        ConstructorInfo? constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        if (constructor == null)
        {
            throw new InvalidOperationException("CrossPlatformSecurityProvider constructor not found.");
        }

        return (IPlatformSecurityProvider)constructor.Invoke(new object[] { appDataPath });
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return "Unknown";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string HashIdentifier(string identifier)
    {
        ReadOnlySpan<byte> identifierBytes = System.Text.Encoding.UTF8.GetBytes(identifier);
        Span<byte> hashBuffer = stackalloc byte[32];
        SHA256.HashData(identifierBytes, hashBuffer);

        return Convert.ToBase64String(hashBuffer)
            .Replace('/', '_')
            .Replace('+', '-');
    }

    private static void PrintLookupHints(string hashedIdentifier)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine($"Credential target: ecliptix:{hashedIdentifier}");
            Console.WriteLine("Credential type: Generic");
            Console.WriteLine("Credential username: com.ecliptix.desktop");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Console.WriteLine("Keychain service: com.ecliptix.desktop");
            Console.WriteLine($"Keychain account: {hashedIdentifier}");
        }
        else if (OperatingSystem.IsLinux())
        {
            Console.WriteLine("Secret schema: com.ecliptix.desktop");
            Console.WriteLine($"Secret attribute: identifier={hashedIdentifier}");
            Console.WriteLine("Secret label: Ecliptix Key Material");
        }
    }
}
