using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    public void InsertSecureKeyChars(int index, string chars)
    {
        if (!_hasSecureKeyBeenTouched)
        {
            _hasSecureKeyBeenTouched = true;
        }

        _secureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public void RemoveSecureKeyChars(int index, int count)
    {
        if (!_hasSecureKeyBeenTouched)
        {
            _hasSecureKeyBeenTouched = true;
        }

        _secureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public void InsertVerifySecureKeyChars(int index, string chars)
    {
        if (!_hasVerifySecureKeyBeenTouched)
        {
            _hasVerifySecureKeyBeenTouched = true;
        }

        _verifySecureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    public void RemoveVerifySecureKeyChars(int index, int count)
    {
        if (!_hasVerifySecureKeyBeenTouched)
        {
            _hasVerifySecureKeyBeenTouched = true;
        }

        _verifySecureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    public async Task HandleEnterKeyPressAsync()
    {
        if (await SubmitCommand.CanExecute.FirstOrDefaultAsync())
        {
            SubmitCommand.Execute().Subscribe();
        }
    }
}
