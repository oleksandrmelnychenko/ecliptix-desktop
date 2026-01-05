using Avalonia.Media.Imaging;
using Ecliptix.Feature.Chats.Chats.ViewModels.Messages;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Chats.Chats.ViewModels;

public class ChannelPostViewModel : MessageViewModelBase
{
    [Reactive] public Bitmap? PostImage { get; set; }
    [Reactive] public int Likes { get; set; }
    [Reactive] public int Comments { get; set; }
    [Reactive] public int Views { get; set; }
    [Reactive] public int Shares { get; set; }
}
