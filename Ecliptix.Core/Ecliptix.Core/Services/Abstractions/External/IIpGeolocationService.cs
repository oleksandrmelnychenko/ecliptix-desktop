using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.External;

public interface IIpGeolocationService
{
    Task<Result<IpCountry, InternalServiceApiFailure>> GetIpCountryAsync(
        CancellationToken cancellationToken = default);
}