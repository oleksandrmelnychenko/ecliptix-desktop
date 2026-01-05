using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Network.Data.Abstractions;
using Ecliptix.Network.Network.Abstractions.Transport;
using Ecliptix.Network.Security.Abstractions;

namespace Ecliptix.Network.Network.Core.Providers;

public sealed record NetworkProviderDependencies(
    IRpcServiceManager RpcServiceManager,
    IApplicationSecureStorageProvider ApplicationSecureStorageProvider,
    ISecureProtocolStateStorage SecureProtocolStateStorage,
    IRpcMetaDataProvider RpcMetaDataProvider,
    IIdentityService IdentityService);
