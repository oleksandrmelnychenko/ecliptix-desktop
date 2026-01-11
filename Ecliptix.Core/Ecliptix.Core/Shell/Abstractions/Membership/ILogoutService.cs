using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;

namespace Ecliptix.Core.Shell.Abstractions.Membership;

public interface ILogoutService
{
    Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason, CancellationToken cancellationToken = default);
}
