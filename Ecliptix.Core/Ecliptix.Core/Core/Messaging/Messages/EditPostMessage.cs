using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Ecliptix.Core.Core.Messaging.Messages;

public sealed class EditPostMessage : ValueChangedMessage<string>
{
    public string PostId { get; }

    public EditPostMessage(string postId) : base(postId)
    {
        PostId = postId;
    }
}
