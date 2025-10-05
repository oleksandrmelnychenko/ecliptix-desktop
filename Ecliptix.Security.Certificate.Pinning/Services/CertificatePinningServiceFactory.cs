namespace Ecliptix.Security.Certificate.Pinning.Services;

public class CertificatePinningServiceFactory : ICertificatePinningServiceFactory
{
    private CertificatePinningService? _service;

    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);

    private bool _disposed;

    public CertificatePinningService? GetOrInitializeService()
    {
        if (_disposed)
            return null;

        if (_service != null)
            return _service;

        _initializationSemaphore.Wait();
        try
        {
            if (_disposed)
                return null;

            if (_service != null)
                return _service;

            CertificatePinningService service = new();
            CertificatePinningOperationResult result = service.Initialize();

            if (result.IsSuccess)
            {
                _service = service;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExitAsync;
                return _service;
            }

            service.DisposeAsync().AsTask().Wait();
            return null;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    private async void OnProcessExitAsync(object? sender, EventArgs e)
    {
        try
        {
            await DisposeAsync();
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _initializationSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            CertificatePinningService? serviceToDispose = _service;
            _service = null;

            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExitAsync;

                if (serviceToDispose != null)
                {
                    await serviceToDispose.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }
        finally
        {
            _initializationSemaphore.Release();
            _initializationSemaphore.Dispose();
        }
    }
}