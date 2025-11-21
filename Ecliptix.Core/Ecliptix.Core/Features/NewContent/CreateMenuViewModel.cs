using System;
using System.Collections.ObjectModel;
using System.Reactive;
using Ecliptix.Core.Core.MVVM;
using ReactiveUI;

namespace Ecliptix.Core.Features.NewContent;

public enum CreateActionType
{
    NewChannel,
    NewGroupChat,
    NewPost
}

public class CreateMenuItem
{
    public string Label { get; set; }
    public string IconPath { get; set; }
    public CreateActionType ActionType { get; set; }
}

public record OpenOverlayWithContentTypeEvent(CreateActionType ActionType);

// Подія для закриття оверлею (можна викликати з будь-якого місця)
public class CloseOverlayEvent
{
}

public class CreateMenuViewModel : ReactiveObject
{
    public ObservableCollection<CreateMenuItem> Items { get; }

    public ReactiveCommand<CreateActionType, CreateActionType> SelectActionCommand { get; }

    public CreateMenuViewModel()
    {

        Items = new ObservableCollection<CreateMenuItem>
        {
            new (){ Label = "New Channel", ActionType = CreateActionType.NewChannel, IconPath = "ChannelIconData" },
            new (){ Label = "New Group Chat", ActionType = CreateActionType.NewGroupChat, IconPath = "GroupIconData" },
            new (){ Label = "New Post", ActionType = CreateActionType.NewPost, IconPath = "PostIconData" }
        };

        SelectActionCommand = ReactiveCommand.Create<CreateActionType, CreateActionType>(type =>
        {
            OnActionSelected(type);
            return type;
        });
    }

    private void OnActionSelected(CreateActionType type)
    {
        Console.WriteLine($"Selected action internal: {type}");
    }

}
