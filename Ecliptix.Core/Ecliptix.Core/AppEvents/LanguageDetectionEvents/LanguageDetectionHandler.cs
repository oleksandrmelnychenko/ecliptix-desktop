using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Serilog;

namespace Ecliptix.Core.AppEvents.LanguageDetectionEvents;

public sealed class LanguageDetectionHandler
{
    private readonly ILocalizationService _localizationService;
    private readonly IBottomSheetEvents _bottomSheetEvents;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;

    public LanguageDetectionHandler(
        ILocalizationService localizationService, 
        IBottomSheetEvents bottomSheetEvents,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider)
    {
        _localizationService = localizationService;
        _bottomSheetEvents = bottomSheetEvents;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;
    }

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
                        _localizationService.SetCulture(e.TargetCulture, () =>
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
            await _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(cultureName);
            _rpcMetaDataProvider.SetCulture(cultureName);
        }
        catch (Exception? ex)
        {
            Log.Error(ex, "Failed to save language settings for culture {Culture}", cultureName);
        }
    }
    private void HideBottomSheet()
    {
        _bottomSheetEvents.BottomSheetChangedState(
            BottomSheetChangedEvent.New(BottomSheetComponentType.Hidden, showScrim: false));
    }
}