using System.Threading.Tasks;

namespace Ecliptix.Core.Services;

public interface IApplicationInitializer
{
    Task<bool> InitializeAsync();
}