using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using Utilities = Ecliptix.Core.Network.Utilities;

namespace Ecliptix.Core.ViewModels.Authentication;

public class SignInViewModel : ViewModelBase, IDisposable, IActivatableViewModel
{
    private readonly NetworkController _networkController;
    private readonly ILocalizationService _localizationService;
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private string _phoneNumber = "+380970177443";
    private readonly PasswordManager? _passwordManager;
    private readonly bool _isPasswordManagerInitialized;

    public string PhoneNumber
    {
        get => _phoneNumber;
        set => this.RaiseAndSetIfChanged(ref _phoneNumber, value);
    }

    public string PasswordHint => _localizationService["Authentication.Registration.passwordConfirmation.passwordHint"];
    private string _errorMessage = string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _isErrorVisible;

    public bool IsErrorVisible
    {
        get => _isErrorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isErrorVisible, value);
    }

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private bool _isPasswordSet;

    public bool IsPasswordSet
    {
        get => _isPasswordSet;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordSet, value);
    }

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    public SignInViewModel(NetworkController networkController, ILocalizationService localizationService)
    {
        _networkController = networkController;
        _localizationService = localizationService;

        Result<PasswordManager, ShieldFailure> passwordManagerCreationResult = PasswordManager.Create();

        if (passwordManagerCreationResult.IsOk)
        {
            _passwordManager = passwordManagerCreationResult.Unwrap();
            _isPasswordManagerInitialized = true;
        }
        else
        {
            _passwordManager = null;
            _isPasswordManagerInitialized = false;
            ErrorMessage =
                $"Critical system error: Password service failed to initialize. {passwordManagerCreationResult.UnwrapErr().Message}";
            IsErrorVisible = true;
        }

        IObservable<bool> canExecuteSignIn = this.WhenAnyValue(
            x => x.PhoneNumber,
            (phone) =>
                !string.IsNullOrWhiteSpace(phone) &&
                _isPasswordManagerInitialized);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync, canExecuteSignIn);
        PhoneNumber = "+380970177443";
    }

    public void UpdatePassword(string? passwordText)
    {
        _securePasswordHandle?.Dispose();
        _securePasswordHandle = null;
        IsPasswordSet = false;
        ErrorMessage = string.Empty;
        IsErrorVisible = false;

        if (!string.IsNullOrEmpty(passwordText))
        {
            Result<SodiumSecureMemoryHandle, ShieldFailure> result = ConvertStringToSodiumHandle(passwordText);
            if (result.IsOk)
            {
                _securePasswordHandle = result.Unwrap();
                IsPasswordSet = true;
            }
            else
            {
                ErrorMessage = $"Error processing password: {result.UnwrapErr().Message}";
                IsErrorVisible = true;
            }
        }
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        IsErrorVisible = false;

        if (!_isPasswordManagerInitialized || _passwordManager == null)
        {
            ErrorMessage = "Sign-in is unavailable due to a system initialization error.";
            IsErrorVisible = true;
            IsBusy = false;
            return;
        }

        if (_securePasswordHandle == null || _securePasswordHandle.IsInvalid || _securePasswordHandle.Length == 0)
        {
            ErrorMessage = "Password is required.";
            IsErrorVisible = true;
            IsBusy = false;
            return;
        }

        byte[]? rentedPasswordBytes = null;
        int passwordBytesLength = _securePasswordHandle.Length;

        try
        {
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(passwordBytesLength);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, passwordBytesLength);
            Result<Protocol.Utilities.Unit, ShieldFailure> readResult = _securePasswordHandle.Read(passwordSpan);

            if (readResult.IsErr)
            {
                ErrorMessage = $"System error: Failed to read password securely. {readResult.UnwrapErr().Message}";
                IsErrorVisible = true;
                IsBusy = false;
                return;
            }

            string passwordString = Encoding.UTF8.GetString(passwordSpan);
            Result<string, ShieldFailure> hashPasswordResult = _passwordManager.HashPassword(passwordString);
            _ = passwordString.Remove(0, passwordString.Length);

            if (hashPasswordResult.IsErr)
            {
                ErrorMessage = $"System error: Password processing failed. {hashPasswordResult.UnwrapErr().Message}";
                IsErrorVisible = true;
                IsBusy = false;
                return;
            }

            string secretKey = hashPasswordResult.Unwrap();
            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            SignInMembershipRequest request = new()
            {
                PhoneNumber = PhoneNumber,
                SecureKey = ByteString.CopyFrom(secretKey, Encoding.ASCII)
            };

           Result<Protocol.Utilities.Unit, ShieldFailure> t = await _networkController.ExecuteServiceAction(
                connectId,
                RcpServiceAction.SignIn,
                request.ToByteArray(),
                ServiceFlowType.Single,
                payload =>
                {
                    SignInMembershipResponse response = Utilities.ParseFromBytes<SignInMembershipResponse>(payload);

                    if (response.Result == SignInMembershipResponse.Types.SignInResult.Succeeded)
                    {
                        IsErrorVisible = false;
                        ErrorMessage = string.Empty;
                        // Navigate to the PassPhase.
                    }
                    else if (response.Result == SignInMembershipResponse.Types.SignInResult.InvalidCredentials)
                    {
                        ErrorMessage = response.HasMessage ? response.Message : "Invalid phone number or password.";
                        IsErrorVisible = true;
                    }
                    else
                    {
                        ErrorMessage = response.HasMessage ? response.Message : "Sign-in failed. Please try again.";
                        IsErrorVisible = true;
                    }

                    return Task.FromResult(new Result<Protocol.Utilities.Unit, ShieldFailure>());
                }
            );

            if (t.IsErr)
            {
                
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An unexpected error occurred during sign-in: {ex.Message}";
            IsErrorVisible = true;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan(0, passwordBytesLength).Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }

            IsBusy = false;
        }
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
            bytesWritten = Encoding.UTF8.GetBytes(text, 0, text.Length, rentedBuffer, 0);

            Result<SodiumSecureMemoryHandle, ShieldFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(bytesWritten);

            if (allocateResult.IsErr)
            {
                return allocateResult;
            }

            newHandle = allocateResult.Unwrap();
            Result<Protocol.Utilities.Unit, ShieldFailure> writeResult =
                newHandle.Write(rentedBuffer.AsSpan(0, bytesWritten));

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

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _securePasswordHandle?.Dispose();
                _securePasswordHandle = null;
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public ViewModelActivator Activator { get; } = new();
}