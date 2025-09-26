using System.Diagnostics.CodeAnalysis;

namespace Ecliptix.Core.Infrastructure.AOT;

/// <summary>
/// Helper to preserve types needed for Avalonia data binding and ReactiveUI in AOT scenarios
/// </summary>
public static class AotAttributeHelper
{
    /// <summary>
    /// Preserves ViewModel types for data binding
    /// </summary>
    public static void PreserveViewModels()
    {
        // Explicit references to ensure ViewModels are preserved
        _ = typeof(Features.Authentication.ViewModels.Hosts.MembershipHostWindowModel);
        _ = typeof(Controls.Modals.DetectLanguageDialogViewModel);
        _ = typeof(Controls.Modals.RedirectNotificationViewModel);
        _ = typeof(Controls.Modals.UserRequestErrorViewModel);
        _ = typeof(Controls.Modals.BottomSheetModal.BottomSheetViewModel);
        _ = typeof(Controls.Core.NetworkStatusNotificationViewModel);
        _ = typeof(Controls.LanguageSelector.LanguageSelectorViewModel);
    }

    /// <summary>
    /// Preserves View types for XAML instantiation
    /// </summary>
    public static void PreserveViews()
    {
        // Preserve available controls and views
        _ = typeof(Controls.Modals.DetectLanguageDialog);
        _ = typeof(Controls.Modals.RedirectNotificationView);
        _ = typeof(Controls.Modals.UserRequestErrorView);
        _ = typeof(Controls.Modals.BottomSheetModal.BottomSheetControl);
        _ = typeof(Controls.Core.NetworkStatusNotification);
        _ = typeof(Controls.LanguageSelector.LanguageSelectorView);
    }

    /// <summary>
    /// Preserves ReactiveUI command types
    /// </summary>
    public static void PreserveReactiveCommands()
    {
        // Preserve ReactiveCommand types for binding
        _ = typeof(ReactiveUI.IReactiveCommand);
        _ = typeof(ReactiveUI.ReactiveObject);
    }
}