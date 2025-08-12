using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.IpGeolocation;

public interface IIpGeolocationService
{
    Task<Result<IpCountry, InternalServiceApiFailure>> GetIpCountryAsync(
        CancellationToken cancellationToken = default);
}