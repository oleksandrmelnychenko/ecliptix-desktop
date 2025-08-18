using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Serilog;

namespace Ecliptix.Core.AppEvents.LanguageDetectionEvents;

public sealed class LanguageDetectionHandler(
    ILocalizationService localizationService,
    IBottomSheetEvents bottomSheetEvents,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IRpcMetaDataProvider rpcMetaDataProvider)
{
    public void Handle(LanguageDetectionDialogEvent e)
    {
        try
        {
            switch (e.Action)
            {
                case LanguageDetectionAction.Decline:
                    Log.Information("User declined language change via DetectLanguageDialog");
                    HideBottomSheet();
                    break;

                case LanguageDetectionAction.Confirm:
                    if (!string.IsNullOrWhiteSpace(e.TargetCulture))
                    {
                        localizationService.SetCulture(e.TargetCulture, () =>
                        {
                            Log.Information("Language changed via DetectLanguageDialog to {Culture}", e.TargetCulture);
                            _ = SaveLanguageSettingsAsync(e.TargetCulture);
                        });
                    }
                    HideBottomSheet();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(e.Action), e.Action, "Unsupported action");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling LanguageDetectionDialogEvent");
        }
    }

    private async Task SaveLanguageSettingsAsync(string? cultureName)
    {
        try
        {
            await applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(cultureName);
            rpcMetaDataProvider.SetCulture(cultureName);
        }
        catch (Exception? ex)
        {
            Log.Error(ex, "Failed to save language settings for culture {Culture}", cultureName);
        }
    }
    private void HideBottomSheet()
    {
        bottomSheetEvents.BottomSheetChangedState(
            BottomSheetChangedEvent.New(BottomSheetComponentType.Hidden, showScrim: false));
    }
}