using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface ISecrecyChannelRpcServices
{
    Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        PubKeyExchange request);

    Task<Result<RestoreSecrecyChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        RestoreSecrecyChannelRequest request);
}