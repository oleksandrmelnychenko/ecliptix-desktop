namespace Ecliptix.Security.Certificate.Pinning.Services;

using Utilities;

public sealed class CertificatePinningServiceFactory : ICertificatePinningServiceFactory
{
    private Option<CertificatePinningService> _service = Option<CertificatePinningService>.None;

    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);

    private bool _disposed;

    public async Task<Option<CertificatePinningService>> GetOrInitializeServiceAsync()
    {
        if (_disposed)
        {
            return Option<CertificatePinningService>.None;
        }

        if (_service.IsSome)
        {
            return _service;
        }

        await _initializationSemaphore.WaitAsync();
        try
        {
            return _disposed ? Option<CertificatePinningService>.None : _service.Or(TryInitializeService);
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    private Option<CertificatePinningService> TryInitializeService()
    {
        CertificatePinningService service = new();
        CertificatePinningOperationResult result = service.Initialize();

        if (result.IsSuccess)
        {
            _service = Option<CertificatePinningService>.Some(service);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExitAsync;
            return _service;
        }

        service.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return Option<CertificatePinningService>.None;
    }

    private async void OnProcessExitAsync(object? sender, EventArgs e)
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception)
        {
            // Finalizer should not throw - swallow disposal exceptions
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _initializationSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Option<CertificatePinningService> serviceToDispose = _service;
            _service = Option<CertificatePinningService>.None;

            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExitAsync;

                if (serviceToDispose.IsSome)
                {
                    await serviceToDispose.Value!.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Disposal errors should not prevent cleanup completion
            }
        }
        finally
        {
            _initializationSemaphore.Release();
            _initializationSemaphore.Dispose();
        }
    }
}
