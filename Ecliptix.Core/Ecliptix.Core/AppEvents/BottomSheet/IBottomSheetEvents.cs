using System;

namespace Ecliptix.Core.AppEvents.BottomSheet;

public interface IBottomSheetEvents
{
    IObservable<BottomSheetChangedEvent> BottomSheetChanged { get; }
    void BottomSheetChangedState(BottomSheetChangedEvent message);
}