using Ecliptix.Network.Services.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Network.Services.External.IpGeolocation;

public interface IIpGeolocationService
{
    Task<Result<IpCountry, InternalServiceApiFailure>> GetIpCountryAsync(
        CancellationToken cancellationToken = default);
}
