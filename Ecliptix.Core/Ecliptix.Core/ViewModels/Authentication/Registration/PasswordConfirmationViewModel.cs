using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using System.Security.Cryptography;
using Ecliptix.Core.Services;
using Ecliptix.Domain.Memberships;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PasswordConfirmationViewModel : ViewModelBase, IActivatableViewModel
{
    
    public ViewModelActivator Activator { get; } = new();
    
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

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SubmitCommand { get; }

    private readonly ILocalizationService _localizationService;
    public string Title => _localizationService["Authentication.Registration.passwordConfirmation.title"];
    public string Description => _localizationService["Authentication.Registration.passwordConfirmation.description"];
    public string PasswordHint => _localizationService["Authentication.Registration.passwordConfirmation.passwordHint"];
    public string VerifyPasswordHint => _localizationService["Authentication.Registration.passwordConfirmation.verifyPasswordHint"];
    public string ButtonContent => _localizationService["Authentication.Registration.passwordConfirmation.button"];
    public string PasswordMismatchError => _localizationService["Authentication.Registration.passwordConfirmation.error.passwordMismatch"];
    
    public PasswordConfirmationViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(
            x => x.CanSubmit,
            x => x.IsBusy,
            (cs, busy) => cs && !busy);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitRegistrationPasswordAsync, canExecuteSubmit);
       
        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(Title));
                    this.RaisePropertyChanged(nameof(Description));
                    this.RaisePropertyChanged(nameof(PasswordHint));
                    this.RaisePropertyChanged(nameof(VerifyPasswordHint));
                    this.RaisePropertyChanged(nameof(ButtonContent));
                    this.RaisePropertyChanged(nameof(PasswordMismatchError));
                })
                .DisposeWith(disposables);

            SubmitCommand.ThrownExceptions
                .Subscribe(ex =>
                {
                    PasswordErrorMessage = $"An unexpected error occurred: {ex.Message}";
                    IsPasswordErrorVisible = true;
                    IsBusy = false;
                })
                .DisposeWith(disposables);
        });
    
        
        
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
                _securePasswordHandle = null;
                PasswordErrorMessage = $"Error processing password: {result.UnwrapErr().Message}";
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
                _secureVerifyPasswordHandle = null;
                PasswordErrorMessage = $"Error processing confirmation password: {result.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
            }
        }

        ValidatePasswords();
    }

    private static Result<SodiumSecureMemoryHandle, ShieldFailure> ConvertStringToSodiumHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return SodiumSecureMemoryHandle.Allocate(0);
        }

        byte[]? rentedBuffer = null;
        SodiumSecureMemoryHandle? newHandle = null;
        int bytesWritten = 0;

        try
        {
            int maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            bytesWritten = Encoding.UTF8.GetBytes(text, rentedBuffer);

            Result<SodiumSecureMemoryHandle, ShieldFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(bytesWritten);

            if (allocateResult.IsErr)
            {
                return allocateResult;
            }

            newHandle = allocateResult.Unwrap();

            Result<Unit, ShieldFailure> writeResult = newHandle.Write(rentedBuffer.AsSpan(0, bytesWritten));
            if (writeResult.IsErr)
            {
                newHandle.Dispose();
                return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(writeResult.UnwrapErr());
            }

            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Ok(newHandle);
        }
        catch (EncoderFallbackException ex)
        {
            newHandle?.Dispose();
            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                ShieldFailure.Decode("Failed to encode password string to UTF-8 bytes.", ex));
        }
        catch (Exception ex)
        {
            newHandle?.Dispose();
            return Result<SodiumSecureMemoryHandle, ShieldFailure>.Err(
                ShieldFailure.Generic("Failed to convert string to secure handle.", ex));
        }
        finally
        {
            if (rentedBuffer != null)
            {
                rentedBuffer.AsSpan(0, bytesWritten).Clear();
                ArrayPool<byte>.Shared.Return(rentedBuffer);
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
            if (isVerifyPasswordEntered)
            {
                PasswordErrorMessage = "Please enter your password in the first field.";
                IsPasswordErrorVisible = true;
            }

            return;
        }

        byte[]? rentedPasswordBytes = null;
        try
        {
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(_securePasswordHandle!.Length);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, _securePasswordHandle.Length);

            Result<Unit, ShieldFailure> readResult = _securePasswordHandle.Read(passwordSpan);
            if (readResult.IsErr)
            {
                PasswordErrorMessage = $"Error processing password: {readResult.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
                return;
            }

            string passwordString = Encoding.UTF8.GetString(passwordSpan);

            _passwordManager ??= PasswordManager.Create().Unwrap();

            Result<Unit, ShieldFailure> complianceResult =
                _passwordManager.CheckPasswordCompliance(passwordString, PasswordPolicy.Default);

            passwordSpan.Clear();
            ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            rentedPasswordBytes = null;

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
                CompareSodiumHandles(_securePasswordHandle, _secureVerifyPasswordHandle!);

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
        catch (DecoderFallbackException ex)
        {
            PasswordErrorMessage = "Password contains invalid characters for string conversion.";
            IsPasswordErrorVisible = true;
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"An unexpected error occurred during validation: {ex.Message}";
            IsPasswordErrorVisible = true;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                // Ensure clear and return if an exception happened before explicit return
                var spanToClear =
                    rentedPasswordBytes.AsSpan(0, _securePasswordHandle?.Length ?? rentedPasswordBytes.Length);
                spanToClear.Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
        }
    }

    private static Result<bool, ShieldFailure> CompareSodiumHandles(
        SodiumSecureMemoryHandle handle1,
        SodiumSecureMemoryHandle handle2)
    {
        if (handle1.IsInvalid || handle2.IsInvalid)
        {
            return Result<bool, ShieldFailure>.Err(
                ShieldFailure.ObjectDisposed("Password handles are invalid for comparison."));
        }

        if (handle1.Length != handle2.Length)
        {
            return Result<bool, ShieldFailure>.Ok(false);
        }

        if (handle1.Length == 0)
        {
            return Result<bool, ShieldFailure>.Ok(true);
        }

        byte[]? rentedBytes1 = null;
        byte[]? rentedBytes2 = null;
        try
        {
            rentedBytes1 = ArrayPool<byte>.Shared.Rent(handle1.Length);
            Span<byte> span1 = rentedBytes1.AsSpan(0, handle1.Length);
            Result<Unit, ShieldFailure> read1Result = handle1.Read(span1);
            if (read1Result.IsErr) return Result<bool, ShieldFailure>.Err(read1Result.UnwrapErr());

            rentedBytes2 = ArrayPool<byte>.Shared.Rent(handle2.Length);
            Span<byte> span2 = rentedBytes2.AsSpan(0, handle2.Length);
            Result<Unit, ShieldFailure> read2Result = handle2.Read(span2);
            if (read2Result.IsErr) return Result<bool, ShieldFailure>.Err(read2Result.UnwrapErr());

            bool areEqual = CryptographicOperations.FixedTimeEquals(span1, span2);
            return Result<bool, ShieldFailure>.Ok(areEqual);
        }
        finally
        {
            if (rentedBytes1 != null)
            {
                rentedBytes1.AsSpan(0, handle1.Length).Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes1);
            }

            if (rentedBytes2 != null)
            {
                rentedBytes2.AsSpan(0, handle2.Length).Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes2);
            }
        }
    }

    private async Task SubmitRegistrationPasswordAsync()
    {
        if (!CanSubmit || _securePasswordHandle is null || _securePasswordHandle.IsInvalid ||
            _securePasswordHandle.Length == 0)
        {
            PasswordErrorMessage = "Submission requirements not met.";
            IsPasswordErrorVisible = true;
            return;
        }

        IsBusy = true;
        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;

        byte[]? rentedPasswordBytes = null;
        byte[]? localSaltForEncryption = null;
        byte[]? localDataEncryptionKey = null;

        try
        {
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(_securePasswordHandle.Length);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, _securePasswordHandle.Length);
            Result<Unit, ShieldFailure> readResult = _securePasswordHandle.Read(passwordSpan);
            if (readResult.IsErr)
            {
                throw new InvalidOperationException(
                    $"Failed to read password for submission: {readResult.UnwrapErr().Message}");
            }

            string passwordString = Encoding.UTF8.GetString(passwordSpan);

            Result<string, ShieldFailure> verifierResult = _passwordManager!.HashPassword(passwordString);
            if (verifierResult.IsErr)
            {
                throw new InvalidOperationException(
                    $"Failed to hash password for server: {verifierResult.UnwrapErr().Message}");
            }

            string passwordVerifierForServer = verifierResult.Unwrap();

          
        

            localSaltForEncryption = GenerateAndPersistLocalSalt("",16);
            localDataEncryptionKey = DeriveLocalKeyFromPassword(passwordString, localSaltForEncryption);

          
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"Submission failed: {ex.Message}";
            IsPasswordErrorVisible = true;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan(0, _securePasswordHandle?.Length ?? 0).Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }

            if (localSaltForEncryption != null)
                Array.Clear(localSaltForEncryption, 0,
                    localSaltForEncryption.Length); // If it was just for derivation and not the persisted one
            if (localDataEncryptionKey != null) Array.Clear(localDataEncryptionKey, 0, localDataEncryptionKey.Length);

            IsBusy = false;
        }
    }

    private byte[] DeriveLocalKeyFromPassword(string password, ReadOnlySpan<byte> salt)
    {
        const int localKeyIterations = 100000;
        const int derivedKeyLength = 32; // For AES-256
        HashAlgorithmName hashAlgo = HashAlgorithmName.SHA256;

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt.ToArray(), localKeyIterations, hashAlgo);
        return pbkdf2.GetBytes(derivedKeyLength);
    }

    private byte[] GenerateAndPersistLocalSalt(string userId, int saltSize)
    {
        // IMPORTANT: This salt MUST be persisted securely and be retrievable
        // next time the user logs in on THIS device to decrypt their E2EE keys.
        // It should not be easily guessable or derivable from public info if possible.
        // Storing it in platform-specific secure storage (Keychain, Keystore) is ideal.
        // This is a placeholder for actual secure salt generation and persistence.
        // For a given user on a given device, this salt should be STABLE.
        byte[] salt = new byte[saltSize];
        // Example: Try to retrieve existing salt first. If not found, generate and store.
        // if (TryRetrieveLocalSalt(userId, out salt)) { return salt; }
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // PersistSaltForUserDevice(userId, salt);
        Console.WriteLine(
            $"[DEBUG] Generated/Retrieved local salt for {userId}: {Convert.ToBase64String(salt)} (NEEDS ACTUAL PERSISTENCE & RETRIEVAL)");
        return salt;
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

// Dummy Proto messages for compilation - replace with your actual generated classes
// Ensure these namespaces match your actual project structure if they exist elsewhere.
/*namespace Protobuf.Registration
{
    public class FinalizeRegistrationRequestProto
    {
        public string? PasswordVerifier { get; set; }
        public Ecliptix.Core.ViewModels.Authentication.Registration.Protobuf.PubKeyExchange.PublicKeyBundle? PublicKeyBundle { get; set; }
        public byte[] ToByteArray() { return Array.Empty<byte>(); /* Implement actual serialization #1# }
    }
    public class FinalizeRegistrationResponseProto
    {
        public string? UserId { get; set; }
    }
}
namespace Protobuf.PubKeyExchange
{
    public class PublicKeyBundle
    {
        public Google.Protobuf.ByteString? IdentityPublicKey { get; set; }
        public int CalculateSize() => IdentityPublicKey?.Length ?? 0;
    }
}*/