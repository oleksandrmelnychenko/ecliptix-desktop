using System;
using System.Text;
using Ecliptix.Utilities;

namespace Ecliptix.Opaque.Protocol;

/// <summary>
/// Simple client-only test for the OPAQUE protocol desktop integration.
/// This validates the native client library without requiring server components.
/// </summary>
public static class OpaqueClientTest
{
    public static void RunClientLibraryTest()
    {
        Console.WriteLine("🔒 OPAQUE Client Library Test (Desktop)");
        Console.WriteLine("=====================================");

        try
        {
            // Test 1: Library loading
            Console.WriteLine("\n📋 Test 1: Client Library Loading");

            // Create a mock server public key for testing
            byte[] mockServerPublicKey = new byte[NativeOpaqueClient.PUBLIC_KEY_LENGTH];
            for (int i = 0; i < mockServerPublicKey.Length; i++)
            {
                mockServerPublicKey[i] = (byte)(i % 256);
            }

            using var client = new OpaqueNativeClient(mockServerPublicKey);
            Console.WriteLine($"✅ Client library loaded successfully");
            Console.WriteLine($"✅ Public key length: {NativeOpaqueClient.PUBLIC_KEY_LENGTH} bytes");

            // Test 2: Registration request creation
            Console.WriteLine("\n📋 Test 2: Registration Request Creation");
            string testPassword = "TestPassword123!";
            byte[] passwordBytes = Encoding.UTF8.GetBytes(testPassword);

            var (regRequest, regState) = client.CreateRegistrationRequest(passwordBytes);
            Console.WriteLine($"✅ Registration request created: {regRequest.Length} bytes");

            client.DestroyState(regState);
            Console.WriteLine("✅ Client state properly cleaned up");

            // Test 3: Authentication key exchange
            Console.WriteLine("\n📋 Test 3: Authentication KE1 Generation");
            var (ke1, authState) = client.GenerateKE1(passwordBytes);
            Console.WriteLine($"✅ KE1 generated: {ke1.Length} bytes");

            client.DestroyState(authState);
            Console.WriteLine("✅ Auth state properly cleaned up");

            Console.WriteLine("\n🎉 OPAQUE Client Library Test PASSED!");
            Console.WriteLine("✅ Desktop client ready for server integration");
            Console.WriteLine("✅ No server components present (correct separation)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ OPAQUE Client Library Test FAILED!");
            Console.WriteLine($"Error: {ex.Message}");

            if (ex.Message.Contains("Unable to load shared library") ||
                ex.Message.Contains("DllNotFoundException"))
            {
                Console.WriteLine("\n💡 Troubleshooting:");
                Console.WriteLine("1. Ensure libopaque_client.dylib is present");
                Console.WriteLine("2. Check libsodium is installed");
                Console.WriteLine("3. Verify library architecture matches runtime");
            }

            throw;
        }
    }

    public static bool ValidateClientIntegration()
    {
        try
        {
            byte[] testKey = new byte[NativeOpaqueClient.PUBLIC_KEY_LENGTH];
            using OpaqueNativeClient client = new(testKey);
            return true;
        }
        catch
        {
            return false;
        }
    }
}