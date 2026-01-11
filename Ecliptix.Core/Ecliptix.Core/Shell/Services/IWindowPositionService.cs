using Avalonia;

namespace Ecliptix.Core.Shell.Services;

public interface IWindowPositionService
{
    bool IsWindowSnapped(PixelPoint position, Size size, Rect workingArea);
    bool ValidateDimensions(double width, double height);
}
