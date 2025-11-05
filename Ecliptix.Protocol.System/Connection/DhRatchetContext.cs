using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Protocol.System.Connection;

internal sealed class DhRatchetContext : IDisposable
{
    public byte[]? DhSecret;
    public byte[]? NewRootKey;
    public byte[]? NewChainKey;
    public byte[]? NewEphemeralPublicKey;
    public byte[]? LocalPrivateKeyBytes;
    public byte[]? NewDhPrivateKeyBytes;
    public SodiumSecureMemoryHandle? NewEphemeralSkHandle;

    public void Dispose()
    {
        WipeIfNotNull(DhSecret);
        WipeIfNotNull(NewRootKey);
        WipeIfNotNull(NewChainKey);
        WipeIfNotNull(NewEphemeralPublicKey);
        WipeIfNotNull(LocalPrivateKeyBytes);
        WipeIfNotNull(NewDhPrivateKeyBytes);
        NewEphemeralSkHandle?.Dispose();
    }

    private static void WipeIfNotNull(byte[]? data)
    {
        if (data is not null)
        {
            SodiumInterop.SecureWipe(data);
        }
    }
}
