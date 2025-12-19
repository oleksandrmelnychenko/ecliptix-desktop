using Ecliptix.Core.Controls.Modals;

namespace Ecliptix.Core.Core.Messaging.Events;

public sealed class ModalHiddenEvent(ModalLayout layout)
{
    public ModalLayout Layout { get; } = layout;
}
