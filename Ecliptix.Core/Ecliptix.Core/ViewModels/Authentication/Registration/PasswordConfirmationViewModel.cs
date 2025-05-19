using System;
using System.Linq;
using ReactiveUI;
using System.Text;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Domain.Memberships;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PasswordConfirmationViewModel : ViewModelBase
{
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private SodiumSecureMemoryHandle? _secureVerifyPasswordHandle;

    private PasswordManager? _passwordManager;

    private string _passwordErrorMessage = string.Empty;

    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
    }

    private bool _isPasswordErrorVisible;

    public bool IsPasswordErrorVisible
    {
        get => _isPasswordErrorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordErrorVisible, value);
    }

    private bool _canSubmit;

    public bool CanSubmit
    {
        get => _canSubmit;
        private set => this.RaiseAndSetIfChanged(ref _canSubmit, value);
    }

    public PasswordConfirmationViewModel()
    {
    }

    public void UpdatePassword(string? passwordText)
    {
        _securePasswordHandle?.Dispose();
        _securePasswordHandle = null;

        if (!string.IsNullOrEmpty(passwordText))
        {
            Result<SodiumSecureMemoryHandle, ShieldFailure> result = ConvertStringToSodiumHandle(passwordText);
            if (result.IsOk)
            {
                _securePasswordHandle = result.Unwrap();
            }
            else
            {
                PasswordErrorMessage = "Error processing password.";
                IsPasswordErrorVisible = true;
            }
        }

        ValidatePasswords();
    }

    public void UpdateVerifyPassword(string? passwordText)
    {
        _secureVerifyPasswordHandle?.Dispose();
        _secureVerifyPasswordHandle = null;

        if (!string.IsNullOrEmpty(passwordText))
        {
            Result<SodiumSecureMemoryHandle, ShieldFailure> result = ConvertStringToSodiumHandle(passwordText);
            if (result.IsOk)
            {
                _secureVerifyPasswordHandle = result.Unwrap();
            }
            else
            {
                PasswordErrorMessage = "Error processing verification password.";
                IsPasswordErrorVisible = true;
            }
        }

        ValidatePasswords();
    }

    private static Result<SodiumSecureMemoryHandle, ShieldFailure> ConvertStringToSodiumHandle(string text)
    {
        byte[]? utf8Bytes = null;
        SodiumSecureMemoryHandle? newHandle = null;
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                Result<SodiumSecureMemoryHandle, ShieldFailure> emptyResult = SodiumSecureMemoryHandle.Allocate(0);
                return emptyResult.IsOk
                    ? emptyResult
                    : Result<SodiumSecureMemoryHandle, ShieldFailure>.Ok(emptyResult.Unwrap());
            }

            utf8Bytes = Encoding.UTF8.GetBytes(text);
            Result<SodiumSecureMemoryHandle, ShieldFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(utf8Bytes.Length);
            if (allocateResult.IsOk)
            {
                return allocateResult;
            }

            newHandle = allocateResult.Unwrap();

            Result<Unit, ShieldFailure> writeResult = newHandle.Write(utf8Bytes);
            if (!writeResult.IsOk) return Result<SodiumSecureMemoryHandle, ShieldFailure>.Ok(newHandle);
            newHandle.Dispose();
            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(writeResult.UnwrapErr());
        }
        catch (Exception ex)
        {
            newHandle?.Dispose();
            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                ShieldFailure.Generic("Failed to convert string to secure handle.", ex));
        }
        finally
        {
            if (utf8Bytes != null)
            {
                Result<Unit, ShieldFailure> wipeResult = SodiumInterop.SecureWipe(utf8Bytes);
                if (wipeResult.IsOk)
                {
                }
            }
        }
    }


    private void ValidatePasswords()
    {
        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;
        CanSubmit = false;

        bool isPasswordEntered = _securePasswordHandle is { IsInvalid: false, Length: > 0 };
        bool isVerifyPasswordEntered = _secureVerifyPasswordHandle is { IsInvalid: false, Length: > 0 };

        if (!isPasswordEntered)
        {
            if (!isVerifyPasswordEntered) return;
            PasswordErrorMessage = "Please enter your password in the first field.";
            IsPasswordErrorVisible = true;

            return;
        }

        byte[]? passwordBytes = null;

        try
        {
            Result<byte[], ShieldFailure> readResult = _securePasswordHandle!.ReadBytes(_securePasswordHandle.Length);
            if (readResult.IsErr)
            {
                PasswordErrorMessage = $"Error processing password: {readResult.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
                return;
            }

            passwordBytes = readResult.Unwrap();
            string? passwordString = Encoding.UTF8.GetString(passwordBytes);

            if (_passwordManager == null)
            {
                Result<PasswordManager, ShieldFailure> pmCreateResult = PasswordManager.Create();
                if (pmCreateResult.IsErr)
                {
                    PasswordErrorMessage = $"Password manager error: {pmCreateResult.UnwrapErr().Message}";
                    IsPasswordErrorVisible = true;
                    return;
                }

                _passwordManager = pmCreateResult.Unwrap();
            }

            Result<Unit, ShieldFailure> complianceResult =
                _passwordManager.CheckPasswordCompliance(passwordString, PasswordPolicy.Default);
            if (complianceResult.IsErr)
            {
                PasswordErrorMessage = complianceResult.UnwrapErr().Message;
                IsPasswordErrorVisible = true;
                return;
            }

            if (!isVerifyPasswordEntered)
            {
                PasswordErrorMessage = "Please verify your password.";
                IsPasswordErrorVisible = true;
                return;
            }

            Result<bool, ShieldFailure> comparisonResult =
                CompareSodiumHandles(_securePasswordHandle!, _secureVerifyPasswordHandle!);

            if (comparisonResult.IsErr)
            {
                PasswordErrorMessage = $"Error comparing passwords: {comparisonResult.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
                return;
            }

            if (!comparisonResult.Unwrap())
            {
                PasswordErrorMessage = "Passwords do not match.";
                IsPasswordErrorVisible = true;
                return;
            }

            IsPasswordErrorVisible = false;
            PasswordErrorMessage = string.Empty;
            CanSubmit = true;
        }
        finally
        {
            if (passwordBytes != null)
            {
                SodiumInterop.SecureWipe(passwordBytes);
            }
        }
    }

    private static Result<bool, ShieldFailure> CompareSodiumHandles(SodiumSecureMemoryHandle handle1,
        SodiumSecureMemoryHandle handle2)
    {
        if (handle1.Length != handle2.Length)
        {
            return Result<bool, ShieldFailure>.Ok(false);
        }

        if (handle1.Length == 0)
        {
            return Result<bool, ShieldFailure>.Ok(true);
        }

        byte[]? bytes1 = null;
        byte[]? bytes2 = null;
        try
        {
            Result<byte[], ShieldFailure> read1Result = handle1.ReadBytes(handle1.Length);
            if (read1Result.IsErr) return Result<bool, ShieldFailure>.Err(read1Result.UnwrapErr());
            bytes1 = read1Result.Unwrap();

            Result<byte[], ShieldFailure> read2Result = handle2.ReadBytes(handle2.Length);
            if (read2Result.IsErr) return Result<bool, ShieldFailure>.Err(read2Result.UnwrapErr());
            bytes2 = read2Result.Unwrap();

            bool areEqual = bytes1.SequenceEqual(bytes2);
            return Result<bool, ShieldFailure>.Ok(areEqual);
        }
        finally
        {
            if (bytes1 != null)
            {
                Result<Unit, ShieldFailure> wipe1Result = SodiumInterop.SecureWipe(bytes1);
                if (wipe1Result.IsErr)
                {
                }

                if (bytes2 != null)
                {
                    Result<Unit, ShieldFailure> wipe2Result = SodiumInterop.SecureWipe(bytes2);
                    if (wipe2Result.IsErr)
                    {
                    }
                }
            }

            handle1.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _securePasswordHandle?.Dispose();
            _secureVerifyPasswordHandle?.Dispose();
            _securePasswordHandle = null;
            _secureVerifyPasswordHandle = null;
        }

        base.Dispose(disposing);
    }
}