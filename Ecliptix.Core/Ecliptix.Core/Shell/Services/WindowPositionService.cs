using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Ecliptix.Core.Shell.Constants;

namespace Ecliptix.Core.Shell.Services;

public sealed class WindowPositionService : IWindowPositionService
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool IsWindowSnapped(PixelPoint position, Size size, Rect workingArea)
    {
        double width = size.Width;
        double height = size.Height;

        bool leftAligned = IsNear(position.X, workingArea.Left);
        bool rightAligned = IsNear(position.X + width, workingArea.Right);
        bool topAligned = IsNear(position.Y, workingArea.Top);
        bool bottomAligned = IsNear(position.Y + height, workingArea.Bottom);

        bool isQuarterWidth = IsNear(width, workingArea.Width * MainWindowConstants.SnapFractions.QUARTER);
        bool isThirdWidth = IsNear(width, workingArea.Width * MainWindowConstants.SnapFractions.THIRD);
        bool isTwoThirdsWidth = IsNear(width, workingArea.Width * MainWindowConstants.SnapFractions.TWO_THIRDS);
        bool isHalfWidth = IsNear(width, workingArea.Width * MainWindowConstants.SnapFractions.HALF);
        bool isFullWidth = IsNear(width, workingArea.Width * MainWindowConstants.SnapFractions.FULL);
        bool isHalfHeight = IsNear(height, workingArea.Height * MainWindowConstants.SnapFractions.HALF);
        bool isFullHeight = IsNear(height, workingArea.Height * MainWindowConstants.SnapFractions.FULL);

        bool isSideAligned = leftAligned || rightAligned;
        bool isVerticalAligned = topAligned || bottomAligned;
        bool isFractionalWidth = isHalfWidth || isThirdWidth || isTwoThirdsWidth || isQuarterWidth;
        bool isCornerAligned = isSideAligned && isVerticalAligned;

        if (topAligned && isSideAligned && isFractionalWidth)
        {
            return true;
        }

        if (isCornerAligned && isHalfWidth && isHalfHeight)
        {
            return true;
        }

        if (topAligned && isFullWidth && isFullHeight)
        {
            return true;
        }

        if (isFullWidth && isHalfHeight && isVerticalAligned)
        {
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ValidateDimensions(double width, double height)
    {
        return width > 0 && height > 0 &&
               width <= MainWindowConstants.Dimensions.FALLBACK_SCREEN_WIDTH * 2 &&
               height <= MainWindowConstants.Dimensions.FALLBACK_SCREEN_HEIGHT * 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNear(double val1, double val2) =>
        Math.Abs(val1 - val2) <= MainWindowConstants.Layout.SNAP_DETECTION_TOLERANCE;
}
