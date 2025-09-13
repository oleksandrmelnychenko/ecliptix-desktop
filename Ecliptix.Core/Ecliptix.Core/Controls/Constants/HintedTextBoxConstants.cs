namespace Ecliptix.Core.Controls.Constants;

public static class HintedTextBoxConstants
{
    public const string FocusColorHex = "#6a5acd";
    public const string ErrorColorHex = "#de1e31";

    public const string InvalidStrengthColorHex = "#ef3a3a";
    public const string VeryWeakStrengthColorHex = "#ff6b35";
    public const string WeakStrengthColorHex = "#ffa500";
    public const string GoodStrengthColorHex = "#f4c20d";
    public const string StrongStrengthColorHex = "#00bcd4";
    public const string VeryStrongStrengthColorHex = "#32cd32";

    public const char DefaultMaskChar = '●';
    public const char NoPasswordChar = '\0';
    public const double DefaultEllipseOpacityVisible = 1.0;
    public const double DefaultEllipseOpacityHidden = 0.0;
    public const double DefaultFontSize = 16.0;
    public const double DefaultWatermarkFontSize = 15.0;

    public const int MaxCachedMaskLength = 100;
    public const int InputDebounceDelayMs = 5;
    public const int InitialCaretIndex = 0;
    public const int TypingAnimationThreshold = 1;
    public const int TypingAnimationDurationMs = 400;
    public const int CacheArrayOffsetIncrement = 1;

    public const double FullOpacity = 1.0;
    public const double ZeroOpacity = 0.0;
    public const double AnimationOpacityBoost = 0.3;

    public const string AnimationStartPercent = "0%";
    public const string AnimationPeakPercent = "30%";
    public const string AnimationEndPercent = "100%";

    public const string MainTextBoxName = "MainTextBox";
    public const string FocusBorderName = "FocusBorder";
    public const string MainBorderName = "MainBorder";
    public const string ShadowBorderName = "ShadowBorder";
    public const string PasswordMaskOverlayName = "PasswordMaskOverlay";

    public const string ClearSecurelyMethodName = "ClearSecurely";
    public const string CollectMethodName = "Collect";
    public const string WaitForPendingFinalizersMethodName = "WaitForPendingFinalizers";

    public const string ErrorShadowKey = "ErrorShadow";
    public const string FocusShadowKey = "FocusShadow";
    public const string DefaultShadowKey = "DefaultShadow";
    public const string InvalidStrengthShadowKey = "InvalidStrengthShadow";
    public const string VeryWeakStrengthShadowKey = "VeryWeakStrengthShadow";
    public const string WeakStrengthShadowKey = "WeakStrengthShadow";
    public const string GoodStrengthShadowKey = "GoodStrengthShadow";
    public const string StrongStrengthShadowKey = "StrongStrengthShadow";
    public const string VeryStrongStrengthShadowKey = "VeryStrongStrengthShadow";
}