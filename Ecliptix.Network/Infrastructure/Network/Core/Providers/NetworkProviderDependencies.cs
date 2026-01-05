using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Abstractions.Network;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderDependencies(
    IRpcServiceManager RpcServiceManager,
    IApplicationSecureStorageProvider ApplicationSecureStorageProvider,
    ISecureProtocolStateStorage SecureProtocolStateStorage,
    IRpcMetaDataProvider RpcMetaDataProvider,
    IIdentityService IdentityService);
