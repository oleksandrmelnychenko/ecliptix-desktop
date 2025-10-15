using System;

namespace Ecliptix.Core.Infrastructure.Network.Core.Constants;

public static class NetworkConstants
{
    public static class Protocol
    {
        public const int DefaultOneTimeKeyCount = 5;
        public const uint OperationIdMinValue = 10;
        public const uint OperationIdReservedRange = 10;
        public const int RequestKeyHexPrefixLength = 16;
    }

    public static class Cryptography
    {
        public const int Sha256HashSize = 32;
    }

    public static class Timeouts
    {
        public static readonly TimeSpan OutageRecoveryTimeout = TimeSpan.FromSeconds(5);
    }

    public static class ErrorMessages
    {
        public const string SessionNotFoundOnServer = "Session not found on server";
    }
}
