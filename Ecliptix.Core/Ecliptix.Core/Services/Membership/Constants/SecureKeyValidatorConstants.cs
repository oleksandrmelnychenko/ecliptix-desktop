using System;
using System.Collections.Frozen;

namespace Ecliptix.Core.Services.Membership.Constants;

public static class SecureKeyValidatorConstants
{
    public static class LocalizationKeys
    {
        public const string NON_ENGLISH_LETTERS = "ValidationErrors.SecureKey.NON_ENGLISH_LETTERS";
        public const string REQUIRED = "ValidationErrors.SecureKey.REQUIRED";
        public const string MIN_LENGTH = "ValidationErrors.SecureKey.MIN_LENGTH";
        public const string NO_UPPERCASE = "ValidationErrors.SecureKey.NO_UPPERCASE";
        public const string NO_LOWERCASE = "ValidationErrors.SecureKey.NO_LOWERCASE";
        public const string NO_SPECIAL_CHAR = "ValidationErrors.SecureKey.NO_SPECIAL_CHAR";
        public const string NO_DIGIT = "ValidationErrors.SecureKey.NO_DIGIT";
        public const string MAX_LENGTH = "ValidationErrors.SecureKey.MAX_LENGTH";
        public const string NO_SPACES = "ValidationErrors.SecureKey.NO_SPACES";
        public const string TOO_SIMPLE = "ValidationErrors.SecureKey.TOO_SIMPLE";
        public const string TOO_COMMON = "ValidationErrors.SecureKey.TOO_COMMON";
        public const string SEQUENTIAL_PATTERN = "ValidationErrors.SecureKey.SEQUENTIAL_PATTERN";
        public const string REPEATED_CHARS = "ValidationErrors.SecureKey.REPEATED_CHARS";
        public const string LACKS_DIVERSITY = "ValidationErrors.SecureKey.LACKS_DIVERSITY";
        public const string CONTAINS_APP_NAME = "ValidationErrors.SecureKey.CONTAINS_APP_NAME";
    }

    public static class ValidationRules
    {
        public const int MIN_LENGTH = 6;
        public const int MAX_LENGTH = 21;
        public const int MIN_CHAR_CLASSES = 2;
        public const double MIN_TOTAL_ENTROPY_BITS = 30;
    }

    public static readonly FrozenSet<string> KeyboardRows = FrozenSet.ToFrozenSet(
    [
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
        "1234567890"
    ]);

    public static readonly FrozenSet<string> AppNameVariants = FrozenSet.ToFrozenSet(
    [
        "ecliptix",
        "eclip",
        "opaque"
    ]);

    public static readonly FrozenSet<string> CommonlyUsedSecureKeys = FrozenSet.ToFrozenSet(
    [
        "123456",
        "password",
        "12345678",
        "123456789",
        "qwerty"
    ], StringComparer.OrdinalIgnoreCase);
}
