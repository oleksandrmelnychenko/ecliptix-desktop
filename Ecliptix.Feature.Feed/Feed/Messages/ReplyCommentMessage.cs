using CommunityToolkit.Mvvm.Messaging.Messages;
using Ecliptix.Feature.Feed.Feed.Domain.Models;

namespace Ecliptix.Feature.Feed.Feed.Messages;

public sealed class ReplyCommentMessage : ValueChangedMessage<Comment>
{
    public ReplyCommentMessage(Comment value) : base(value)
    {
    }
}
