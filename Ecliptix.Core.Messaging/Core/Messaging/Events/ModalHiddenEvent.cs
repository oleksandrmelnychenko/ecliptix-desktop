using Ecliptix.Core.Messaging.Controls.Modals;

namespace Ecliptix.Core.Messaging.Core.Messaging.Events;

public sealed class ModalHiddenEvent(ModalLayout layout)
{
    public ModalLayout Layout { get; } = layout;
}
