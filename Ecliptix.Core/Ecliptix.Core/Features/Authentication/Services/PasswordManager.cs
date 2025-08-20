using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Core.Features.Authentication.Services;

public sealed class PasswordManager
{
    private const int DefaultSaltSize = 16;
    private const int DefaultIterations = 600_000;
    private const char HashSeparator = ':';

    private static readonly Regex LowercaseRegex = new("[a-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UppercaseRegex = new("[A-Z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DigitRegex = new(@"\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AlphanumericOnlyRegex =
        new("^[a-zA-Z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly int _iterations;
    private readonly HashAlgorithmName _hashAlgorithmName;
    private readonly int _saltSize;

    private PasswordManager(int iterations, HashAlgorithmName hashAlgorithmName, int saltSize)
    {
        _iterations = iterations;
        _hashAlgorithmName = hashAlgorithmName;
        _saltSize = saltSize;
    }

    public static Result<PasswordManager, EcliptixProtocolFailure> Create(
        int iterations = DefaultIterations,
        HashAlgorithmName? hashAlgorithmName = null,
        int saltSize = DefaultSaltSize)
    {
        if (iterations <= 0)
        {
            return Result<PasswordManager, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("PasswordManager configuration: Iterations must be a positive integer."));
        }

        if (saltSize <= 0)
        {
            return Result<PasswordManager, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("PasswordManager configuration: Salt size must be a positive integer."));
        }

        HashAlgorithmName effectiveHashAlgorithm = hashAlgorithmName ?? HashAlgorithmName.SHA256;

        if (effectiveHashAlgorithm != HashAlgorithmName.SHA1 &&
            effectiveHashAlgorithm != HashAlgorithmName.SHA256 &&
            effectiveHashAlgorithm != HashAlgorithmName.SHA384 &&
            effectiveHashAlgorithm != HashAlgorithmName.SHA512)
        {
            return Result<PasswordManager, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"PasswordManager configuration: Unsupported hash algorithm '{effectiveHashAlgorithm.Name}'. Supported for PBKDF2 are SHA1, SHA256, SHA384, SHA512."));
        }

        return Result<PasswordManager, EcliptixProtocolFailure>.Ok(new PasswordManager(iterations, effectiveHashAlgorithm,
            saltSize));
    }

    public Result<Unit, EcliptixProtocolFailure> CheckPasswordCompliance(
        string password,
        PasswordPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy, nameof(policy));

        List<string> validationErrorMessages = [];

        if (string.IsNullOrEmpty(password))
        {
            validationErrorMessages.Add("Password cannot be empty.");
        }
        else
        {
            if (password.Length < policy.MinLength)
                validationErrorMessages.Add($"Password must be at least {policy.MinLength} characters long.");
            if (policy.RequireLowercase && !LowercaseRegex.IsMatch(password))
                validationErrorMessages.Add("Password must contain at least one lowercase letter.");
            if (policy.RequireUppercase && !UppercaseRegex.IsMatch(password))
                validationErrorMessages.Add("Password must contain at least one uppercase letter.");
            if (policy.RequireDigit && !DigitRegex.IsMatch(password))
                validationErrorMessages.Add("Password must contain at least one digit.");

            if (policy.RequireSpecialChar && !string.IsNullOrEmpty(policy.AllowedSpecialChars))
            {
                string specialCharPattern = $"[{Regex.Escape(policy.AllowedSpecialChars)}]";
                if (!Regex.IsMatch(password, specialCharPattern))
                    validationErrorMessages.Add(
                        $"Password must contain at least one special character from the set: {policy.AllowedSpecialChars}.");
            }

            if (policy.EnforceAllowedCharsOnly)
            {
                if (!string.IsNullOrEmpty(policy.AllowedSpecialChars))
                {
                    string allAllowedCharsPattern = $"^[a-zA-Z0-9{Regex.Escape(policy.AllowedSpecialChars)}]*$";
                    if (!Regex.IsMatch(password, allAllowedCharsPattern))
                        validationErrorMessages.Add(
                            "Password contains characters that are not allowed. Only alphanumeric and specified special characters are permitted.");
                }
                else
                {
                    if (!AlphanumericOnlyRegex.IsMatch(password))
                        validationErrorMessages.Add(
                            "Password contains characters that are not allowed. Only alphanumeric characters are permitted.");
                }
            }
        }

        if (validationErrorMessages.Count != 0)
        {
            string combinedReasons = string.Join("; ", validationErrorMessages);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput($"Password does not meet complexity requirements: {combinedReasons}"));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private byte[] GenerateSalt()
    {
        byte[] salt = new byte[_saltSize];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    public Result<string, EcliptixProtocolFailure> HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Result<string, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Password to hash cannot be null or empty."));
        }

        try
        {
            using Rfc2898DeriveBytes pbkdf2 = new(password, [], _iterations, _hashAlgorithmName);
            byte[] hash = pbkdf2.GetBytes(GetHashSizeForAlgorithm(_hashAlgorithmName));
            return Result<string, EcliptixProtocolFailure>.Ok(
                $"{HashSeparator}{Convert.ToBase64String(hash)}");
        }
        catch (Exception ex)
        {
            return Result<string, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey("An unexpected error occurred during PBKDF2 password hashing.", ex));
        }
    }

    public Result<Unit, EcliptixProtocolFailure> VerifyPassword(string password, string hashedPasswordWithSalt)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Input password for verification cannot be null or empty."));
        }

        if (string.IsNullOrEmpty(hashedPasswordWithSalt))
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Stored hash for verification cannot be null or empty."));
        }

        string[] parts = hashedPasswordWithSalt.Split(HashSeparator);
        if (parts.Length != 2)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode("Stored hash is not in the expected 'salt:hash' format."));
        }

        byte[] salt;
        byte[] storedHash;

        try
        {
            salt = Convert.FromBase64String(parts[0]);
            storedHash = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException ex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode("Failed to decode Base64 components from stored hash.", ex));
        }

        if (salt.Length != _saltSize)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Stored salt size ({salt.Length} bytes) does not match configured salt size ({_saltSize} bytes)."));
        }

        try
        {
            using Rfc2898DeriveBytes pbkdf2 = new(password, salt, _iterations, _hashAlgorithmName);
            int expectedHashSize = GetHashSizeForAlgorithm(_hashAlgorithmName);

            if (storedHash.Length != expectedHashSize)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput(
                        $"Stored hash size ({storedHash.Length} bytes) does not match expected size for {_hashAlgorithmName.Name} ({expectedHashSize} bytes)."));
            }

            byte[] testHash = pbkdf2.GetBytes(expectedHashSize);

            if (CryptographicOperations.FixedTimeEquals(testHash, storedHash))
            {
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }

            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Password verification failed: Hashes do not match."));
        }
        catch (Exception ex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(
                    "An unexpected error occurred during PBKDF2 derivation or comparison for verification.", ex));
        }
    }

    private static int GetHashSizeForAlgorithm(HashAlgorithmName algName)
    {
        return algName.Name switch
        {
            _ when algName.Name == HashAlgorithmName.SHA1.Name => 20,
            _ when algName.Name == HashAlgorithmName.SHA256.Name => 32,
            _ when algName.Name == HashAlgorithmName.SHA384.Name => 48,
            _ when algName.Name == HashAlgorithmName.SHA512.Name => 64,
            _ => throw new NotSupportedException(
                $"Hash size not defined for algorithm '{algName.Name}'. This indicates an internal configuration issue and should have been caught during PasswordManager creation.")
        };
    }
}