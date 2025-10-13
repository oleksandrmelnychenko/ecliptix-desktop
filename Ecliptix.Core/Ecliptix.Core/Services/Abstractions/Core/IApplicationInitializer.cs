using System.Threading.Tasks;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IApplicationInitializer
{
    Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings);
}
