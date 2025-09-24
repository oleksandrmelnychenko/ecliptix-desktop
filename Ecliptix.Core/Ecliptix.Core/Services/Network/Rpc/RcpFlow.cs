using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Rpc;

public abstract class RpcFlow
{
    public static RpcFlow NewEmptyInboundStream()
    {
        async IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }

        return new InboundStream(EmptyStream());
    }

    public static RpcFlow NewDrainOutboundSink()
    {
        return new OutboundSink(new DrainSink());
    }

    public static RpcFlow NewBidirectionalStream()
    {
        Channel<SecureEnvelope> channel = Channel.CreateUnbounded<SecureEnvelope>();
        IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> inbound = ToOkStream(channel.Reader);
        ChannelSink outboundSink = new(channel.Writer);
        return new BidirectionalStream(inbound, outboundSink);
    }

    private static IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> ToOkStream(
        ChannelReader<SecureEnvelope> reader)
    {
        return reader.ReadAllAsync().Select(payload => Result<SecureEnvelope, NetworkFailure>.Ok(payload));
    }

    public class SingleCall(Task<Result<SecureEnvelope, NetworkFailure>> result) : RpcFlow
    {
        public SingleCall(Result<SecureEnvelope, NetworkFailure> result)
            : this(Task.FromResult(result))
        {
        }

        public Task<Result<SecureEnvelope, NetworkFailure>> Result { get; } = result;
    }

    public class InboundStream(IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> stream)
        : RpcFlow
    {
        public IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> Stream { get; } = stream;
    }

    public class OutboundSink(IOutboundSink sink) : RpcFlow
    {
        public IOutboundSink Sink { get; } = sink;
    }

    public class BidirectionalStream(
        IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> inbound,
        IOutboundSink outboundSink)
        : RpcFlow
    {
        public IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> Inbound { get; } = inbound;

        public new IOutboundSink OutboundSink { get; } = outboundSink;
    }
}
internal class DrainSink : IOutboundSink
{
    public Task<Result<Unit, NetworkFailure>> SendAsync(SecureEnvelope payload)
    {
        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
    }
}

internal class ChannelSink(ChannelWriter<SecureEnvelope> writer) : IOutboundSink
{
    public async Task<Result<Unit, NetworkFailure>> SendAsync(SecureEnvelope payload)
    {
        try
        {
            await writer.WriteAsync(payload);
            return Result<Unit, NetworkFailure>.Ok(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }
}