using System;
using System.Buffers;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Ecliptix.Utilities.Membership;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships.SignIn;

public sealed class SignInViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private string _phoneNumber = string.Empty;
    private string _passwordErrorMessage = string.Empty;
    private int _currentPasswordLength;
    private bool _isErrorVisible;
    private bool _isBusy;
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private bool _isDisposed;

    public string UrlPathSegment => "/sign-in";
    public IScreen HostScreen { get; }

    public int CurrentPasswordLength
    {
        get => _currentPasswordLength;
        private set => this.RaiseAndSetIfChanged(ref _currentPasswordLength, value);
    }
    
    public string PhoneNumber
    {
        get => _phoneNumber;
        set => this.RaiseAndSetIfChanged(ref _phoneNumber, value);
    }

    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
    }

    public bool IsErrorVisible
    {
        get => _isErrorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isErrorVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }
    
    public bool IsPasswordSet => _securePasswordHandle is { IsInvalid: false };
    
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SignInCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AccountRecoveryCommand { get; }

    public SignInViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen) : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        IObservable<bool> canExecute = this.WhenAnyValue(
                x => x.PhoneNumber,
                x => x.PasswordErrorMessage,
                x => x.CurrentPasswordLength,
                (number, error, passwordLength) =>
                    string.IsNullOrWhiteSpace(MembershipValidation.Validate(ValidationType.MobileNumber, number)) &&
                    passwordLength >= 8 &&
                    string.IsNullOrEmpty(error))
            .Throttle(TimeSpan.FromMilliseconds(20))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync, canExecute);
        AccountRecoveryCommand = ReactiveCommand.Create(() => { /* Navigation Logic Here */ });
    }

    public void InsertPasswordChars(int index, string chars)
    {
        if (string.IsNullOrEmpty(chars)) return;
        ClearError();
        ModifySecurePassword(index, 0, chars);
    }

    public void RemovePasswordChars(int index, int count)
    {
        if (count <= 0) return;
        ClearError();
        ModifySecurePassword(index, count, string.Empty);
    }

    private void ModifySecurePassword(int index, int removeCount, string insertChars)
    {
        byte[]? oldPasswordBytes = null;
        byte[]? newPasswordBytes = null;
        SodiumSecureMemoryHandle? newHandle = null;

        try
        {
            int oldLength = _securePasswordHandle?.Length ?? 0;
            if (oldLength > 0)
            {
                oldPasswordBytes = ArrayPool<byte>.Shared.Rent(oldLength);
                Result<Unit, SodiumFailure> readResult = _securePasswordHandle!.Read(oldPasswordBytes.AsSpan(0, oldLength));
                if (readResult.IsErr)
                {
                    SetError($"System Error: {readResult.UnwrapErr().Message}");
                    return;
                }
            }
            var oldSpan = oldLength > 0 ? oldPasswordBytes.AsSpan(0, oldLength) : ReadOnlySpan<byte>.Empty;

            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            if (index > oldSpan.Length) index = oldSpan.Length;
            removeCount = Math.Min(removeCount, oldSpan.Length - index);

            int newLength = oldSpan.Length - removeCount + insertBytes.Length;
            if (newLength > 0)
            {
                newPasswordBytes = ArrayPool<byte>.Shared.Rent(newLength);
                var newSpan = newPasswordBytes.AsSpan(0, newLength);

                oldSpan.Slice(0, index).CopyTo(newSpan);
                insertBytes.CopyTo(newSpan.Slice(index));
                if (index + removeCount < oldSpan.Length)
                {
                    oldSpan.Slice(index + removeCount).CopyTo(newSpan.Slice(index + insertBytes.Length));
                }
            }
            
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult = SodiumSecureMemoryHandle.Allocate(newLength);
            if (allocateResult.IsErr)
            {
                SetError($"System Error: {allocateResult.UnwrapErr().Message}");
                return;
            }
            newHandle = allocateResult.Unwrap();

            if (newLength > 0)
            {
                Result<Unit, SodiumFailure> writeResult = newHandle.Write(newPasswordBytes.AsSpan(0, newLength));
                if (writeResult.IsErr)
                {
                    SetError($"System Error: {writeResult.UnwrapErr().Message}");
                    newHandle.Dispose();
                    return;
                }
            }
            
            var oldHandle = _securePasswordHandle;
            _securePasswordHandle = newHandle;
            oldHandle?.Dispose();
            newHandle = null; 

            CurrentPasswordLength = _securePasswordHandle.Length;
        }
        finally
        {
            if (oldPasswordBytes != null) ArrayPool<byte>.Shared.Return(oldPasswordBytes, true);
            if (newPasswordBytes != null) ArrayPool<byte>.Shared.Return(newPasswordBytes, true);
            newHandle?.Dispose();
        }
    }
    
    private async Task SignInAsync()
    {
        IsBusy = true;
        ClearError();

        if (_securePasswordHandle == null || _securePasswordHandle.IsInvalid)
        {
            SetError("Password is required.");
            IsBusy = false;
            return;
        }

        byte[]? rentedPasswordBytes = null;
        try
        {
            int passwordLength = _securePasswordHandle.Length;
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(passwordLength);
            var passwordSpan = rentedPasswordBytes.AsSpan(0, passwordLength);

            Result<Unit, SodiumFailure> readResult = _securePasswordHandle.Read(passwordSpan);
            if (readResult.IsErr)
            {
                SetError($"System error: Failed to read password securely. {readResult.UnwrapErr().Message}");
                return;
            }
            
            //READ the password and convert it to a string for further processing
            string password = Encoding.UTF8.GetString(passwordSpan);

            // Your existing OPAQUE protocol and network logic goes here.
            // It will operate on the `passwordSpan` variable.
        }
        catch (Exception ex)
        {
            SetError($"An unexpected error occurred: {ex.Message}");
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
            IsBusy = false;
        }
    }
    
    private void SetError(string message)
    {
        PasswordErrorMessage = message;
        IsErrorVisible = true;
    }

    private void ClearError()
    {
        PasswordErrorMessage = string.Empty;
        IsErrorVisible = false;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _securePasswordHandle?.Dispose();
        }
        _isDisposed = true;
    }

    ~SignInViewModel()
    {
        Dispose(false);
    }
}