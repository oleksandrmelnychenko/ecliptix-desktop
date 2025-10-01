using System;
using System.Collections.Frozen;

namespace Ecliptix.Core.Services.Membership.Constants;

public static class SecureKeyValidatorConstants
{
    public static class LocalizationKeys
    {
        public const string NonEnglishLetters = "ValidationErrors.SecureKey.NonEnglishLetters";
        public const string Required = "ValidationErrors.SecureKey.Required";
        public const string MinLength = "ValidationErrors.SecureKey.MinLength";
        public const string NoUppercase = "ValidationErrors.SecureKey.NoUppercase";
        public const string NoLowercase = "ValidationErrors.SecureKey.NoLowercase";
        public const string NoSpecialChar = "ValidationErrors.SecureKey.NoSpecialChar";
        public const string NoDigit = "ValidationErrors.SecureKey.NoDigit";
        public const string MaxLength = "ValidationErrors.SecureKey.MaxLength";
        public const string NoSpaces = "ValidationErrors.SecureKey.NoSpaces";
        public const string TooSimple = "ValidationErrors.SecureKey.TooSimple";
        public const string TooCommon = "ValidationErrors.SecureKey.TooCommon";
        public const string SequentialPattern = "ValidationErrors.SecureKey.SequentialPattern";
        public const string RepeatedChars = "ValidationErrors.SecureKey.RepeatedChars";
        public const string LacksDiversity = "ValidationErrors.SecureKey.LacksDiversity";
        public const string ContainsAppName = "ValidationErrors.SecureKey.ContainsAppName";
    }

    public static class ValidationRules
    {
        public const int MinLength = 6;
        public const int MaxLength = 21;
        public const int MinCharClasses = 2;
        public const double MinTotalEntropyBits = 30;
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

    public static readonly FrozenSet<string> CommonlyUsedPasswords = FrozenSet.ToFrozenSet(
    [
        "123456",
        "password",
        "12345678",
        "123456789",
        "qwerty"
    ], StringComparer.OrdinalIgnoreCase);
}
