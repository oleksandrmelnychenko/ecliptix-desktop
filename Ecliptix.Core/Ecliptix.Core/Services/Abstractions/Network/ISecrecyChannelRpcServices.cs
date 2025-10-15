using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface ISecrecyChannelRpcServices
{
    Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecureEnvelope request,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default);

    Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default);
}
