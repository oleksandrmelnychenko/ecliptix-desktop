using System.Threading;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Protocol.System.Utilities;

namespace Ecliptix.Core.Services.Security;

public class ServerPublicKeyProvider(NetworkProvider networkProvider) : IServerPublicKeyProvider
{
    private readonly Lock _lock = new();
    private byte[]? _cachedKey;

    public byte[] GetServerPublicKey()
    {
        lock (_lock)
        {
            if (_cachedKey != null)
            {
                return _cachedKey;
            }

            _cachedKey = SecureByteStringInterop.WithByteStringAsSpan(
                networkProvider.ApplicationInstanceSettings.ServerPublicKey,
                span => span.ToArray());

            return _cachedKey;
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _cachedKey = null;
        }
    }
}
