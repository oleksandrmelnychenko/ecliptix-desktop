using System.Threading.Tasks;
using Ecliptix.Core.Services.Core;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IKeyDiagnosticsService
{
    Task<KeyStorageDiagnostics> DiagnoseKeyStorageAsync(string membershipId);
}
