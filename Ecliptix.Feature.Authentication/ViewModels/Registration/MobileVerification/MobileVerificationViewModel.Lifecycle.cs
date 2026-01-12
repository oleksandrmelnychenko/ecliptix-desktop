using System;
using System.Threading;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    public void ResetState()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelCurrentOperation();
        RawMobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
        _executionErrorSubject.OnNext(string.Empty);
    }

    public new void Dispose() => Dispose(true);

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelCurrentOperation();
            _executionErrorSubject.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(
            ref _cancellationTokenSource,
            null);

        if (operationSource == null)
        {
            return;
        }

        try
        {
            operationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {

        }
        finally
        {
            operationSource.Dispose();
        }
    }
}
