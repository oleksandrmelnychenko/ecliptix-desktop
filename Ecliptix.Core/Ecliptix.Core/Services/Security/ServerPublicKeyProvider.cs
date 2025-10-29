using System;
using System.Threading;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Security;

internal sealed class ServerPublicKeyProvider(NetworkProvider networkProvider) : IServerPublicKeyProvider
{
    private readonly Lock _lock = new();
    private Option<byte[]> _cachedKey = Option<byte[]>.None;

    public byte[] GetServerPublicKey()
    {
        lock (_lock)
        {
            Option<byte[]> key = _cachedKey.Or(() =>
            {
                byte[] newKey = SecureByteStringInterop.WithByteStringAsSpan(
                    networkProvider.ApplicationInstanceSettings.ServerPublicKey,
                    span => span.ToArray());
                _cachedKey = Option<byte[]>.Some(newKey);
                return _cachedKey;
            });

            if (!key.IsSome)
            {
                throw new InvalidOperationException("Failed to load server public key");
            }

            return (byte[])key.Value!.Clone();
        }
    }
}
