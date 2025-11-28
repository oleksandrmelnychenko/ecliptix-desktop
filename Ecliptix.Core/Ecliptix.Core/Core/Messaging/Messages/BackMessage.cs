using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Ecliptix.Core.Core.Messaging.Messages;

public sealed class BackMessage : ValueChangedMessage<bool>
{
    public BackMessage(bool value) : base(value)
    {
    }
}
