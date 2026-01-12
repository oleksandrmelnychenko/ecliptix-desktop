using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SecureKeyValidator = Ecliptix.Feature.Authentication.Services.Membership.SecureKeyValidator;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    private IObservable<bool> SetupValidation()
    {
        IObservable<SystemU> languageTrigger = LanguageChanged;

        IObservable<SystemU> lengthTrigger = this
            .WhenAnyValue(x => x.CurrentSecureKeyLength, x => x.CurrentVerifySecureKeyLength)
            .Do(lengths => Serilog.Log.Information(
                "[SECURE-KEY-VM] Key lengths changed: SecureKey={SecureKeyLength}, VerifyKey={VerifyKeyLength}",
                lengths.Item1, lengths.Item2))
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<bool> isSecureKeyLogicallyValid = SetupSecureKeyValidation(validationTrigger);
        IObservable<bool> secureKeysMatch = SetupVerifyKeyValidation(validationTrigger);

        return isSecureKeyLogicallyValid
            .CombineLatest(secureKeysMatch, (isSecureKeyValid, areMatching) =>
            {
                bool result = isSecureKeyValid && areMatching;
                return result;
            })
            .DistinctUntilChanged();
    }

    private IObservable<bool> SetupSecureKeyValidation(IObservable<SystemU> validationTrigger)
    {
        IObservable<(string? ERROR, List<(string Text, bool IsMet)> Checklist, SecureKeyStrength Strength, bool IsSuccess)> validationResult = validationTrigger
            .StartWith(SystemU.Default)
            .Select(_ => ValidateSecureKeyInternal())
            .Replay(1)
            .RefCount();

        validationResult
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(v =>
            {
                for (int i = 0; i < v.Checklist.Count && i < ValidationTips.Count; i++)
                {
                    ValidationTips[i].IsMet = v.Checklist[i].IsMet;
                }
            })
            .DisposeWith(_disposables);

        validationResult.Select(v => v.IsSuccess).ToPropertyEx(this, x => x.IsSecureKeySuccess);
        validationResult.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentSecureKeyStrength);

        validationResult.Select(v =>
        {
            if (!string.IsNullOrEmpty(v.ERROR))
            {
                return v.ERROR;
            }

            return _hasSecureKeyBeenTouched ? FormatSecureKeyStrengthMessage(v.Strength, null, null) : string.Empty;
        }).ToPropertyEx(this, x => x.SecureKeyStrengthMessage);

        this.WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => _hasSecureKeyBeenTouched)
            .ToPropertyEx(this, x => x.HasSecureKeyBeenTouched);

        this.WhenAnyValue(x => x.SecureKeyStrengthMessage)
            .Subscribe(m => SecureKeyError = m)
            .DisposeWith(_disposables);

        return validationResult.Select(v => v.IsSuccess);
    }

    private IObservable<bool> SetupVerifyKeyValidation(IObservable<SystemU> validationTrigger)
    {
        IObservable<bool> secureKeysMatch = validationTrigger
            .Select(_ => DoSecureKeysMatch())
            .Replay(1)
            .RefCount();

        IObservable<string> verifySecureKeyErrorStream = secureKeysMatch
            .Select(match =>
            {
                bool shouldShowError = _hasVerifySecureKeyBeenTouched && !match;
                return shouldShowError
                    ? LocalizationService[AuthenticationConstants.VERIFY_SECURE_KEY_DOES_NOT_MATCH_KEY]
                    : string.Empty;
            })
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(VALIDATION_THROTTLE_MS))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        verifySecureKeyErrorStream
            .Subscribe(error => VerifySecureKeyError = error)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VerifySecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasVerifySecureKeyError = flag)
            .DisposeWith(_disposables);

        return secureKeysMatch;
    }

    private (string? ERROR, List<(string Text, bool IsMet)> Checklist, SecureKeyStrength Strength, bool IsSuccess) ValidateSecureKeyInternal()
    {
        string? error = null;
        List<(string Description, bool IsMet)> checklist = new();
        SecureKeyStrength strength = SecureKeyStrength.INVALID;
        bool isSuccess = false;

        _secureKeyBuffer.WithSecureBytes(bytes =>
        {
            string secureKey = Encoding.UTF8.GetString(bytes);
            checklist = SecureKeyValidator.GetChecklistStatus(secureKey, LocalizationService);
            strength = SecureKeyValidator.EstimateSecureKeyStrength(secureKey, LocalizationService);
            isSuccess = checklist.All(x => x.IsMet);
        });

        return (error, checklist, strength, isSuccess);
    }

    private bool DoSecureKeysMatch()
    {
        if (_secureKeyBuffer.Length != _verifySecureKeyBuffer.Length)
        {
            return false;
        }

        if (_secureKeyBuffer.Length == 0)
        {
            return true;
        }

        int length = _secureKeyBuffer.Length;
        byte[] secureKeyArray = ArrayPool<byte>.Shared.Rent(length);
        byte[] verifyArray = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            _secureKeyBuffer.WithSecureBytes(secureKeyBytes => { secureKeyBytes.CopyTo(secureKeyArray.AsSpan()); });
            _verifySecureKeyBuffer.WithSecureBytes(verifyBytes => { verifyBytes.CopyTo(verifyArray.AsSpan()); });

            return CryptographicOperations.FixedTimeEquals(
                secureKeyArray.AsSpan(0, length),
                verifyArray.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(secureKeyArray, clearArray: true);
            ArrayPool<byte>.Shared.Return(verifyArray, clearArray: true);
        }
    }

    private string FormatSecureKeyStrengthMessage(SecureKeyStrength strength, string? error, string? recommendations)
    {
        string strengthText = strength switch
        {
            SecureKeyStrength.INVALID => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_INVALID_KEY],
            SecureKeyStrength.VERY_WEAK => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_VERY_WEAK_KEY],
            SecureKeyStrength.WEAK => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_WEAK_KEY],
            SecureKeyStrength.GOOD => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_GOOD_KEY],
            SecureKeyStrength.STRONG => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_STRONG_KEY],
            SecureKeyStrength.VERY_STRONG => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_VERY_STRONG_KEY],
            _ => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_INVALID_KEY]
        };

        string message = !string.IsNullOrEmpty(error) ? error : (recommendations ?? string.Empty);
        return string.IsNullOrEmpty(message) ? strengthText : $"{strengthText}: {message}";
    }
}
