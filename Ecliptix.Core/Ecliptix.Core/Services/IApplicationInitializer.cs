using System.Threading.Tasks;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Services;

public interface IApplicationInitializer
{
    Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings);

    bool IsMembershipConfirmed { get; }
}