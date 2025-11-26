using Avalonia.Media.Imaging;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class ChannelPostViewModel : MessageViewModelBase
{
    [Reactive] public Bitmap? PostImage { get; set; }
    [Reactive] public int Likes { get; set; }
    [Reactive] public int Comments { get; set; }
    [Reactive] public int Views { get; set; }
    [Reactive] public int Shares { get; set; }
}
