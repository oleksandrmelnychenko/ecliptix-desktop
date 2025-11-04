using System;
using System.Collections.Concurrent;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Authentication.Internal;

internal sealed class RegistrationStateManager : IDisposable
{
    private readonly ConcurrentDictionary<string, RegistrationResult> _registrationStates = new();
    private bool _isDisposed;

    public bool TryAddRegistration(ByteString membershipIdentifier, RegistrationResult registrationResult)
    {
        if (_isDisposed)
        {
            return false;
        }

        string key = CreateKey(membershipIdentifier);
        return _registrationStates.TryAdd(key, registrationResult);
    }

    public bool TryRemoveRegistration(ByteString membershipIdentifier, out RegistrationResult? registrationResult)
    {
        string key = CreateKey(membershipIdentifier);
        return _registrationStates.TryRemove(key, out registrationResult);
    }

    public void CleanupRegistration(ByteString membershipIdentifier)
    {
        if (TryRemoveRegistration(membershipIdentifier, out RegistrationResult? result))
        {
            result?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (System.Collections.Generic.KeyValuePair<string, RegistrationResult> kvp in _registrationStates)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[REGISTRATION-STATE] Failed to dispose registration result for key {Key}", kvp.Key);
            }
        }

        _registrationStates.Clear();
    }

    private static string CreateKey(ByteString membershipIdentifier) =>
        Convert.ToBase64String(membershipIdentifier.Span);
}
