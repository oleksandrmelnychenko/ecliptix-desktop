using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Rpc;

public abstract class RpcFlow
{
    public static RpcFlow NewEmptyInboundStream()
    {
        async IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> EmptyStream()
        {
            await Task.CompletedTask; // Satisfy async requirement
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
        Channel<CipherPayload> channel = Channel.CreateUnbounded<CipherPayload>();
        IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> inbound = ToOkStream(channel.Reader);
        ChannelSink outboundSink = new(channel.Writer);
        return new BidirectionalStream(inbound, outboundSink);
    }

    private static IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> ToOkStream(
        ChannelReader<CipherPayload> reader)
    {
        return reader.ReadAllAsync().Select(payload => Result<CipherPayload, NetworkFailure>.Ok(payload));
    }

    public class SingleCall(Task<Result<CipherPayload, NetworkFailure>> result) : RpcFlow
    {
        public SingleCall(Result<CipherPayload, NetworkFailure> result)
            : this(Task.FromResult(result))
        {
        }

        public Task<Result<CipherPayload, NetworkFailure>> Result { get; } = result;
    }

    public class InboundStream(IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> stream)
        : RpcFlow
    {
        public IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> Stream { get; } = stream;
    }

    public class OutboundSink(IOutboundSink sink) : RpcFlow
    {
        public IOutboundSink Sink { get; } = sink;
    }

    public class BidirectionalStream(
        IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> inbound,
        IOutboundSink outboundSink)
        : RpcFlow
    {
        public IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> Inbound { get; } = inbound;

        public new IOutboundSink OutboundSink { get; } = outboundSink;
    }
}
internal class DrainSink : IOutboundSink
{
    public Task<Result<Unit, NetworkFailure>> SendAsync(CipherPayload payload)
    {
        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
    }
}

internal class ChannelSink(ChannelWriter<CipherPayload> writer) : IOutboundSink
{
    public async Task<Result<Unit, NetworkFailure>> SendAsync(CipherPayload payload)
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