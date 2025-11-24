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
    public string IconGeometryData { get; set; }
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
            new (){
                Label = "New Channel",
                ActionType = CreateActionType.NewChannel,
                IconGeometryData = "M5.41,21L6.12,17H2.12L2.47,15H6.47L7.53,9H3.53L3.88,7H7.88L8.59,3H10.59L9.88,7H15.88L16.59,3H18.59L17.88,7H21.88L21.53,9H17.53L16.47,15H20.47L20.12,17H16.12L15.41,21H13.41L14.12,17H8.12L7.41,21H5.41M9.53,9L8.47,15H14.47L15.53,9H9.53Z"
            },
            new (){
                Label = "New Group Chat",
                ActionType = CreateActionType.NewGroupChat,
                IconGeometryData = "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"
            },
            new (){
                Label = "New Post",
                ActionType = CreateActionType.NewPost,
                IconGeometryData = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"
            }
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
