using Avalonia.Media;

namespace Ecliptix.Core.Modularity.Abstractions.Navigation;

public interface INavigationMenuItem
{
    string Title { get; }
    Geometry? IconData { get; }
    bool IsSelected { get; set; }
}
