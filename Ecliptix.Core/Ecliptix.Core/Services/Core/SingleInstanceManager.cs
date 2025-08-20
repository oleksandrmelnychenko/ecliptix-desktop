using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Services.Core;

/// <summary>
/// Cross-platform single instance manager using named mutex on Windows and file locking on Unix systems
/// </summary>
public class SingleInstanceManager : ISingleInstanceManager
{
    private readonly ILogger<SingleInstanceManager> _logger;
    private readonly string _instanceId;
    private readonly string _lockFilePath;
    private readonly string _signalFilePath;
    
    private Mutex? _mutex;
    private FileStream? _lockFileStream;
    private Timer? _signalWatcher;
    private bool _disposed;
    
    private const string ApplicationId = "EcliptixDesktop";
    private const int SignalCheckInterval = 1000; // 1 second
    
    public event EventHandler? InstanceActivationRequested;
    
    public SingleInstanceManager(ILogger<SingleInstanceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instanceId = $"{ApplicationId}_{Environment.UserName}";
        
        string tempPath = Path.GetTempPath();
        _lockFilePath = Path.Combine(tempPath, $"{_instanceId}.lock");
        _signalFilePath = Path.Combine(tempPath, $"{_instanceId}.signal");
    }
    
    public bool TryAcquireInstance()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SingleInstanceManager));
            
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryAcquireInstanceWindows();
            }
            else
            {
                return TryAcquireInstanceUnix();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire single instance lock");
            return true; // Fail open - allow the application to start
        }
    }
    
    public bool NotifyExistingInstance()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SingleInstanceManager));
            
        try
        {
            // Create signal file to notify existing instance
            File.WriteAllText(_signalFilePath, DateTimeOffset.UtcNow.ToString("O"));
            _logger.LogDebug("Created signal file to notify existing instance");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to signal existing instance");
            return false;
        }
    }
    
    private bool TryAcquireInstanceWindows()
    {
        try
        {
            _mutex = new Mutex(true, _instanceId, out bool createdNew);
            
            if (!createdNew)
            {
                _logger.LogInformation("Another instance is already running (Windows mutex)");
                _mutex.Dispose();
                _mutex = null;
                return false;
            }
            
            _logger.LogDebug("Acquired single instance lock using Windows mutex");
            StartSignalWatcher();
            return true;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed, we can take over
            _logger.LogWarning("Previous instance crashed, taking over mutex");
            StartSignalWatcher();
            return true;
        }
    }
    
    private bool TryAcquireInstanceUnix()
    {
        try
        {
            // Try to create and lock the file exclusively
            _lockFileStream = new FileStream(
                _lockFilePath, 
                FileMode.Create, 
                FileAccess.ReadWrite, 
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose
            );
            
            // Write our process ID to the lock file
            using (var writer = new StreamWriter(_lockFileStream, leaveOpen: true))
            {
                writer.WriteLine(Environment.ProcessId);
                writer.WriteLine(DateTimeOffset.UtcNow.ToString("O"));
                writer.Flush();
            }
            
            _logger.LogDebug("Acquired single instance lock using Unix file locking");
            StartSignalWatcher();
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogInformation("Another instance is already running (Unix file lock): {Error}", ex.Message);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogInformation("Another instance is already running (access denied): {Error}", ex.Message);
            return false;
        }
    }
    
    private void StartSignalWatcher()
    {
        // Clean up any existing signal file
        try
        {
            if (File.Exists(_signalFilePath))
                File.Delete(_signalFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up existing signal file");
        }
        
        // Start watching for signal file creation
        _signalWatcher = new Timer(CheckForSignal, null, SignalCheckInterval, SignalCheckInterval);
        _logger.LogDebug("Started signal watcher");
    }
    
    private void CheckForSignal(object? state)
    {
        try
        {
            if (File.Exists(_signalFilePath))
            {
                _logger.LogInformation("Received activation signal from another instance");
                
                // Clean up the signal file
                try
                {
                    File.Delete(_signalFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up signal file");
                }
                
                // Notify that we should bring the window to foreground
                Task.Run(() => InstanceActivationRequested?.Invoke(this, EventArgs.Empty));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for activation signal");
        }
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        
        try
        {
            _signalWatcher?.Dispose();
            _mutex?.Dispose();
            _lockFileStream?.Dispose();
            
            // Clean up signal file
            if (File.Exists(_signalFilePath))
            {
                try
                {
                    File.Delete(_signalFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up signal file during disposal");
                }
            }
            
            _logger.LogDebug("Single instance manager disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing single instance manager");
        }
    }
}