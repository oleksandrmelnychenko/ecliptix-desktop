using Avalonia;

namespace Ecliptix.Core.Views.Core.Services;

public interface IWindowPositionService
{
    bool IsWindowSnapped(PixelPoint position, Size size, Rect workingArea);
    bool ValidateDimensions(double width, double height);
}
