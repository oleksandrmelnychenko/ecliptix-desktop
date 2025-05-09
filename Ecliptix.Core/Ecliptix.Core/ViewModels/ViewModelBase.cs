using System;
using Ecliptix.Core.Network;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core.ViewModels;

public class ViewModelBase : ReactiveObject
{
    protected uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;

        uint connectId = Network.Utilities.ComputeUniqueConnectId(
            appInstanceInfo.AppInstanceId,
            appInstanceInfo.DeviceId, pubKeyExchangeType);

        return connectId;
    }

    protected Guid? SystemAppDeviceId()
    {
        AppInstanceInfo appInstanceInfo = Locator.Current.GetService<AppInstanceInfo>()!;
        return appInstanceInfo.SystemAppDeviceId;
    }
}