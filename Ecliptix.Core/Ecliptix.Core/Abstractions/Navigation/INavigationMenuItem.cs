using Avalonia.Media;

namespace Ecliptix.Core.Modularity.Navigation;

public interface INavigationMenuItem
{
    string Title { get; }
    Geometry? IconData { get; }
    bool IsSelected { get; set; }
}
