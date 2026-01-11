using CommunityToolkit.Mvvm.Messaging.Messages;
using Ecliptix.Feature.Feed.Domain.Models;

namespace Ecliptix.Feature.Feed.ViewModels.Messages;

public sealed class ReplyCommentMessage : ValueChangedMessage<Comment>
{
    public ReplyCommentMessage(Comment value) : base(value)
    {
    }
}
