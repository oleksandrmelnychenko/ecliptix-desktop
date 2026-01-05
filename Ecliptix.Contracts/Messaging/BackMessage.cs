using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Ecliptix.Contracts.Messaging;

public sealed class BackMessage : ValueChangedMessage<bool>
{
    public BackMessage(bool value) : base(value)
    {
    }
}
