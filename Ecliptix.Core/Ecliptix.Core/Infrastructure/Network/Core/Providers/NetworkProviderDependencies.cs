using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Services.Abstractions.Network;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderDependencies(
    IRpcServiceManager RpcServiceManager,
    IApplicationSecureStorageProvider ApplicationSecureStorageProvider,
    ISecureProtocolStateStorage SecureProtocolStateStorage,
    IRpcMetaDataProvider RpcMetaDataProvider);
