using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.AppEvents.LanguageDetectionEvents;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Serilog;

namespace Ecliptix.Core.AppEvents.BottomSheet;

public record BottomSheetChangedEvent
{
    public UserControl? Control { get; init; }
    public BottomSheetComponentType ComponentType { get; init; }

    public bool ShowScrim { get; init; }

    private BottomSheetChangedEvent(BottomSheetComponentType componentType, bool showScrim, UserControl? userControl)
    {
        ShowScrim = showScrim;
        ComponentType = componentType;
        Control = userControl;
    }

    public static BottomSheetChangedEvent
        New(BottomSheetComponentType componentType, bool showScrim = true, UserControl? userControl = null)
    {
        return new(componentType, showScrim, userControl);
    }
}

public sealed class BottomSheetEvents
    : IBottomSheetEvents, ILanguageDetectionEvents
{
    private readonly IEventAggregator _aggregator;
    private readonly ILocalizationService _localizationService;
    private readonly LanguageDetectionHandler _languageDetectionHandler;
    private readonly ISystemEvents _systemEvents;
    private readonly NetworkProvider _networkProvider;
    private readonly Func<UserControl> _languageDetectionModalFactory;

    public BottomSheetEvents(
        IEventAggregator aggregator,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        NetworkProvider networkProvider)
    {
        _aggregator = aggregator;
        _localizationService = localizationService;
        _languageDetectionHandler = new LanguageDetectionHandler(
            localizationService,
            this,
            applicationSecureStorageProvider,
            rpcMetaDataProvider);

        BottomSheetChanged = aggregator.GetEvent<BottomSheetChangedEvent>();
        LanguageDetectionRequested = aggregator.GetEvent<LanguageDetectionDialogEvent>();
        LanguageDetectionRequested.Subscribe(_languageDetectionHandler.Handle);

        _languageDetectionModalFactory = () =>
        {
            DetectLanguageDialog dialog = new();
            dialog.SetLocalizationService(_localizationService, this, networkProvider);
            return dialog;
        };
    }

    public IObservable<BottomSheetChangedEvent> BottomSheetChanged { get; }
    public IObservable<LanguageDetectionDialogEvent> LanguageDetectionRequested { get; }


    public void Invoke(LanguageDetectionDialogEvent languageDetectionEvent)
    {
        _aggregator.Publish(languageDetectionEvent);
    }

    public void BottomSheetChangedState(BottomSheetChangedEvent message)
    {
        Log.Information("BottomSheetChangedState called with ComponentType={ComponentType}, ShowScrim={ShowScrim}, Control={@Control}",
            message.ComponentType, message.ShowScrim, message.Control);
        UserControl? userControl = message.Control ?? GetBottomSheetControl(message.ComponentType);
        _aggregator.Publish(BottomSheetChangedEvent.New(message.ComponentType, message.ShowScrim, userControl));
    }

    private UserControl? GetBottomSheetControl(BottomSheetComponentType componentType)
    {
        return componentType switch
        {
            BottomSheetComponentType.DetectedLocalization => _languageDetectionModalFactory(),
            BottomSheetComponentType.Hidden => null,
            _ => throw new ArgumentOutOfRangeException(nameof(componentType))
        };
    }
}