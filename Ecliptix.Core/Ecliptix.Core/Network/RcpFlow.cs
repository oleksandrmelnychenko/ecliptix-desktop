using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network;

/// <summary>
///     Represents different modes of RPC operations in the network service layer.
/// </summary>
public abstract class RpcFlow
{
    /// <summary>
    ///     Creates an empty inbound stream that yields no payloads.
    /// </summary>
    /// <returns>An RpcFlow.InboundStream with an empty stream.</returns>
    public static RpcFlow NewEmptyInboundStream()
    {
        async IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> EmptyStream()
        {
            yield break; // Produces an empty async enumerable
        }

        return new InboundStream(EmptyStream());
    }

    /// <summary>
    ///     Creates a draining outbound sink that discards all sent payloads.
    /// </summary>
    /// <returns>An RpcFlow.OutboundSink that discards all data.</returns>
    public static RpcFlow NewDrainOutboundSink()
    {
        return new OutboundSink(new DrainSink());
    }

    /// <summary>
    ///     Creates a new bidirectional streaming channel with an unbounded buffer.
    /// </summary>
    /// <returns>An RpcFlow.BidirectionalStream with an inbound stream and an outbound sink.</returns>
    public static RpcFlow NewBidirectionalStream()
    {
        Channel<CipherPayload> channel = Channel.CreateUnbounded<CipherPayload>();
        IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> inbound = ToOkStream(channel.Reader);
        ChannelSink outboundSink = new(channel.Writer);
        return new BidirectionalStream(inbound, outboundSink);
    }

    // Helper method to convert ChannelReader payloads to Result.Ok
    private static IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> ToOkStream(
        ChannelReader<CipherPayload> reader)
    {
        return reader.ReadAllAsync().Select(payload => Result<CipherPayload, NetworkFailure>.Ok(payload));
    }

    /// <summary>
    ///     Represents a single-call response containing a single payload or an error.
    /// </summary>
    public class SingleCall : RpcFlow
    {
        /// <summary>
        ///     Initializes a new instance of the SingleCall class.
        /// </summary>
        /// <param name="result">The result task.</param>
        public SingleCall(Task<Result<CipherPayload, NetworkFailure>> result)
        {
            Result = result;
        }

        /// <summary>
        ///     Convenience constructor for immediate results.
        /// </summary>
        /// <param name="result">The immediate result.</param>
        public SingleCall(Result<CipherPayload, NetworkFailure> result)
            : this(Task.FromResult(result))
        {
        }

        /// <summary>
        ///     The result of the single call, wrapped in a task for consistency with other variants.
        /// </summary>
        public Task<Result<CipherPayload, NetworkFailure>> Result { get; }
    }

    /// <summary>
    ///     Represents an inbound stream providing payloads from the server.
    /// </summary>
    public class InboundStream : RpcFlow
    {
        /// <summary>
        ///     Initializes a new instance of the InboundStream class.
        /// </summary>
        /// <param name="stream">The inbound stream.</param>
        public InboundStream(IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> stream)
        {
            Stream = stream;
        }

        /// <summary>
        ///     The asynchronous stream of payloads or errors.
        /// </summary>
        public IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> Stream { get; }
    }

    /// <summary>
    ///     Represents an outbound sink accepting payloads to be sent to the server.
    /// </summary>
    public class OutboundSink : RpcFlow
    {
        /// <summary>
        ///     Initializes a new instance of the OutboundSink class.
        /// </summary>
        /// <param name="sink">The outbound sink.</param>
        public OutboundSink(IOutboundSink sink)
        {
            Sink = sink;
        }

        /// <summary>
        ///     The sink for sending payloads.
        /// </summary>
        public IOutboundSink Sink { get; }
    }

    /// <summary>
    ///     Represents a bidirectional streaming channel with an inbound stream and an outbound sink.
    /// </summary>
    public class BidirectionalStream : RpcFlow
    {
        /// <summary>
        ///     Initializes a new instance of the BidirectionalStream class.
        /// </summary>
        /// <param name="inbound">The inbound stream.</param>
        /// <param name="outboundSink">The outbound sink.</param>
        public BidirectionalStream(
            IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> inbound,
            IOutboundSink outboundSink)
        {
            Inbound = inbound;
            OutboundSink = outboundSink;
        }

        /// <summary>
        ///     The inbound stream of received payloads.
        /// </summary>
        public IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> Inbound { get; }

        /// <summary>
        ///     The outbound sink for transmitting payloads.
        /// </summary>
        public new IOutboundSink OutboundSink { get; }
    }
}

/// <summary>
///     A sink that discards all payloads and always succeeds.
/// </summary>
internal class DrainSink : IOutboundSink
{
    /// <summary>
    ///     Discards the payload and returns a successful result.
    /// </summary>
    /// <param name="payload">The payload to discard.</param>
    /// <returns>A task with a successful result.</returns>
    public Task<Result<Unit, NetworkFailure>> SendAsync(CipherPayload payload)
    {
        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
    }
}

/// <summary>
///     A sink that writes payloads to a channel.
/// </summary>
internal class ChannelSink : IOutboundSink
{
    private readonly ChannelWriter<CipherPayload> _writer;

    /// <summary>
    ///     Initializes a new instance of the ChannelSink class.
    /// </summary>
    /// <param name="writer">The channel writer to send payloads to.</param>
    public ChannelSink(ChannelWriter<CipherPayload> writer)
    {
        _writer = writer;
    }

    /// <summary>
    ///     Sends a payload to the channel.
    /// </summary>
    /// <param name="payload">The payload to send.</param>
    /// <returns>A task representing the result of the send operation.</returns>
    public async Task<Result<Unit, NetworkFailure>> SendAsync(CipherPayload payload)
    {
        try
        {
            await _writer.WriteAsync(payload);
            return Result<Unit, NetworkFailure>.Ok(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }
}