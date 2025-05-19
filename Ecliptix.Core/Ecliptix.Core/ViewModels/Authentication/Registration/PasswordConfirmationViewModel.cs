using System;
using System.Linq;
using ReactiveUI;
using System.Text;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PasswordConfirmationViewModel : ViewModelBase
{
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private SodiumSecureMemoryHandle? _secureVerifyPasswordHandle;

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
            var result = ConvertStringToSodiumHandle(passwordText);
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
        bool isPasswordPresent = _securePasswordHandle is { Length: > 0 };
        bool isVerifyPasswordPresent = _secureVerifyPasswordHandle is { Length: > 0 };

        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;

        switch (isPasswordPresent)
        {
            case false when !isVerifyPasswordPresent:
                CanSubmit = false;
                return;
            case false:
                PasswordErrorMessage = "Please enter your password.";
                IsPasswordErrorVisible = true;
                CanSubmit = false;
                return;
        }

        if (!isVerifyPasswordPresent)
        {
            PasswordErrorMessage = "Please verify your password.";
            IsPasswordErrorVisible = true;
            CanSubmit = false;
            return;
        }

        Result<bool, ShieldFailure> comparisonResult =
            CompareSodiumHandles(_securePasswordHandle!, _secureVerifyPasswordHandle!);
        if (comparisonResult.IsErr)
        {
            PasswordErrorMessage = "Error comparing passwords.";
            IsPasswordErrorVisible = true;
            CanSubmit = false;
            return;
        }

        if (!comparisonResult.Unwrap())
        {
            PasswordErrorMessage = "Passwords do not match";
            IsPasswordErrorVisible = true;
            CanSubmit = false;
        }
        else
        {
            IsPasswordErrorVisible = false;
            CanSubmit = true;
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

