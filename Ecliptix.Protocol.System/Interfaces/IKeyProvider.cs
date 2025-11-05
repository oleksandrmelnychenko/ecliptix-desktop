using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Interfaces;

internal interface IKeyProvider
{
    Result<T, EcliptixProtocolFailure> ExecuteWithKey<T>(uint keyIndex, Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation);
}
