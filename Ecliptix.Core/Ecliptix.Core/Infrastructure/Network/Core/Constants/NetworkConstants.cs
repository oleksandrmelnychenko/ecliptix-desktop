using System;

namespace Ecliptix.Core.Infrastructure.Network.Core.Constants;

public static class NetworkConstants
{
    public static class Protocol
    {
        public const int DEFAULT_ONE_TIME_KEY_COUNT = 5;
        public const uint OPERATION_ID_MIN_VALUE = 10;
        public const uint OPERATION_ID_RESERVED_RANGE = 10;
        public const int REQUEST_KEY_HEX_PREFIX_LENGTH = 16;
    }

    public static class Timeouts
    {
        public static readonly TimeSpan OutageRecoveryTimeout = TimeSpan.FromSeconds(5);
    }

    public static class ErrorMessages
    {
        public const string SESSION_NOT_FOUND_ON_SERVER = "Session not found on server";
    }
}
