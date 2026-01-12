using System;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Settings;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    private void SetupSubscriptions()
    {
        LanguageChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => AttemptAutoSwitchCountry())
            .DisposeWith(_disposables);

        if (_messageBus != null)
        {
            _messageBus.Subscribe<CountryCodeSelectedEvent>(evt =>
                {
                    if (evt.RequestorContext != CountryPickerContext)
                    {
                        return Task.CompletedTask;
                    }

                    _hasManualCountrySelection = true;
                    CountryFlag = evt.SelectedCountry.FlagImagePath;
                    PhonePrefix = evt.SelectedCountry.PhonePrefix;
                    CountryIso = evt.SelectedCountry.IsoCode;
                    return Task.CompletedTask;
                })
                .DisposeWith(_disposables);
        }

    }

    private void AttemptAutoSwitchCountry()
    {
        if (_hasManualCountrySelection)
        {
            return;
        }

        if (!string.IsNullOrEmpty(RawMobileNumber))
        {
            return;
        }

        try
        {
            CultureInfo culture = LocalizationService.CurrentCultureInfo;

            (string Iso, string Prefix, string FlagPath)? countryData = AppCultureSettings.Default.ResolveCountryFromCulture(culture);

            if (countryData == null)
            {
                return;
            }

            CountryIso = countryData.Value.Iso;
            PhonePrefix = countryData.Value.Prefix;
            CountryFlag = countryData.Value.FlagPath;
        }
        catch
        {
            // ignored
        }
    }
}
