using CommunityToolkit.Mvvm.Messaging.Messages;
using Ecliptix.Core.Features.Feed.Models;

namespace Ecliptix.Core.Core.Messaging.Messages;

public sealed class ReplyCommentMessage : ValueChangedMessage<Comment>
{
    public ReplyCommentMessage(Comment value) : base(value)
    {
    }
}
