namespace Ecliptix.Core.Controls;

public class BackgroundColorChangedMessage
{
    public string ColorHex { get; }
    public double Opacity { get; }

    public BackgroundColorChangedMessage(string colorHex, double opacity = 0.7)
    {
        ColorHex = colorHex;
        Opacity = opacity;
    }
}
