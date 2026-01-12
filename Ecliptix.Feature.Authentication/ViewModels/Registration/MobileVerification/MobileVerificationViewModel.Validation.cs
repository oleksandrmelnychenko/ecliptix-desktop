using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using MobileNumberValidator = Ecliptix.Feature.Authentication.Services.Membership.MobileNumberValidator;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    private IObservable<bool> SetupValidation()
    {
        IObservable<Unit> languageTrigger = LanguageChanged;

        languageTrigger
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(Title));
                this.RaisePropertyChanged(nameof(Description));
                this.RaisePropertyChanged(nameof(Hint));
                this.RaisePropertyChanged(nameof(Watermark));
                this.RaisePropertyChanged(nameof(ButtonText));
            })
            .DisposeWith(_disposables);

        IObservable<Unit> mobileTrigger = this
            .WhenAnyValue(x => x.RawMobileNumber)
            .Select(_ => Unit.Default);

        IObservable<Unit> validationTrigger =
            mobileTrigger
                .Merge(languageTrigger);

        IObservable<string> mobileValidation = validationTrigger
            .Select(_ => MobileNumberValidator.Validate(RawMobileNumber, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.RawMobileNumber)
            .CombineLatest(mobileValidation, (mobile, validationError) =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                {
                    _hasMobileNumberBeenTouched = true;
                }

                return !_hasMobileNumberBeenTouched ? string.Empty : validationError;
            })
            .Replay(1)
            .RefCount();

        mobileErrorStream
            .Subscribe(error =>
            {
                MobileNumberError = error;
                HasMobileNumberError = !string.IsNullOrEmpty(error);
            })
            .DisposeWith(_disposables);

        return mobileValidation
            .Select(string.IsNullOrEmpty)
            .DistinctUntilChanged();
    }
}
