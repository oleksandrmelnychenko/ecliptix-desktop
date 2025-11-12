using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Models.Navigation;

public sealed partial class NavigationMenuItem : ReactiveObject
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string TooltipText { get; init; } = string.Empty;
    public NavigationMenuItemType Type { get; init; } = NavigationMenuItemType.Regular;

    [Reactive] public bool IsSelected { get; set; }
}

public enum NavigationMenuItemType
{
    Regular,
    Bottom
}
