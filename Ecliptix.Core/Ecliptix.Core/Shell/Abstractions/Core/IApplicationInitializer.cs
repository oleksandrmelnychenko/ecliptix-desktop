using System.Threading.Tasks;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Shell.Abstractions.Core;

public enum ApplicationInitializationResult
{
    SUCCESS,
    SETTINGS_INITIALIZATION_FAILED,
    SECRECY_CHANNEL_FAILED,
    DEVICE_REGISTRATION_FAILED
}

public interface IApplicationInitializer
{
    Task<ApplicationInitializationResult> InitializeAsync(DefaultSystemSettings defaultSystemSettings);
}
