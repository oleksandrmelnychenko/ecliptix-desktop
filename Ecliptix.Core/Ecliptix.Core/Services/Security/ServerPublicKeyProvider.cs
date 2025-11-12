using System;
using System.Threading;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Services.Security;

internal sealed class ServerPublicKeyProvider(NetworkProvider networkProvider) : IServerPublicKeyProvider
{
    private const int EXPECTED_KEY_LENGTH = 32;
    private readonly Lock _lock = new();
    private Option<byte[]> _cachedKey = Option<byte[]>.None;

    public byte[] GetServerPublicKey()
    {
        lock (_lock)
        {
            if (_cachedKey.IsSome)
            {
                return (byte[])_cachedKey.Value!.Clone();
            }

            byte[] newKey = SecureByteStringInterop.WithByteStringAsSpan(
                networkProvider.ApplicationInstanceSettings.ServerPublicKey,
                span => span.ToArray());

            if (newKey.Length != EXPECTED_KEY_LENGTH)
            {
                string errorMessage = $"Server public key has invalid length. Expected: {EXPECTED_KEY_LENGTH} bytes, Got: {newKey.Length} bytes. " +
                                    "This indicates the server public key was not properly initialized during authentication. " +
                                    "Please restart the application and sign in again.";

                Log.Error(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            _cachedKey = Option<byte[]>.Some(newKey);

            return (byte[])_cachedKey.Value!.Clone();
        }
    }
}
