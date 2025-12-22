using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Account;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

using SystemU = System.Reactive.Unit;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public class AccountSettingsViewModel : Core.MVVM.ViewModelBase, IActivatableViewModel
{
    private readonly IApplicationSecureStorageProvider _secureStorage;

    private bool _isInternalUpdate;

    [Reactive] public string DisplayName { get; set; }
    [Reactive] public string ProfileName { get; set; }
    [Reactive] public string AccountId { get; set; }
    [Reactive] public string ProfileId { get; set; }
    [Reactive] public string ProfileInitials { get; set; }
    [Reactive] public bool IsLoadingProfile { get; set; }

    [Reactive] public bool IsSavedMessageVisible { get; set; }
    public ReactiveCommand<SystemU, SystemU> SaveChangesCommand { get; private set; }

    public ReactiveCommand<SystemU, SystemU> ChangeAvatarCommand { get; private set; }

    public ViewModelActivator Activator { get; } = new();

    public AccountSettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider secureStorageProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService)
    {
        _secureStorage = secureStorageProvider;

        SaveChangesCommand = ReactiveCommand.CreateFromTask(ExecuteSaveAsync);

        ChangeAvatarCommand = ReactiveCommand.Create(() =>
        {
            Log.Information("Change avatar clicked");
        });

        this.WhenActivated(disposables =>
        {

            this.WhenAnyValue(x => x.DisplayName)
                .Select(name => GetInitials(name))
                .Subscribe(initials => ProfileInitials = initials)
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.DisplayName)
                .Skip(1)
                .Where(_ => !_isInternalUpdate)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .DistinctUntilChanged()
                .Throttle(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Select(_ => SystemU.Default)
                .InvokeCommand(SaveChangesCommand)
                .DisposeWith(disposables);

            LoadUserProfileAsync(CancellationToken.None)
                .ConfigureAwait(false);


        });
    }

    private async Task ExecuteSaveAsync()
    {
        try
        {
            await Task.Delay(300);

            ShowSavedMessage();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private void ShowSavedMessage()
    {

        IsSavedMessageVisible = true;

        Observable.Timer(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => IsSavedMessageVisible = false);
    }

    private async Task LoadUserProfileAsync(CancellationToken cancellationToken)
    {
        IsLoadingProfile = true;
        _isInternalUpdate = true;
        try
        {
            Option<Guid> accountIdOpt = await GetCurrentAccountIdAsync();

            if (!accountIdOpt.IsSome)
            {
                Log.Warning("[SETTINGS-VM] Cannot load profile: No active account session.");
                return;
            }

            Guid currentAccountId = accountIdOpt.Value;

            ByteString accountId = Helpers.GuidToByteString(currentAccountId);
            GetAccountProfileRequest request = new()
            {
                CurrentAccountId = accountId,
                ByAccountId = accountId
            };

            TaskCompletionSource<GetAccountProfileResponse> responseSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            Result<EUnit, NetworkFailure> networkResult = await NetworkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.GetAccountProfile,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    GetAccountProfileResponse response = Helpers.ParseFromBytes<GetAccountProfileResponse>(payload);
                    responseSource.TrySetResult(response);
                    return Task.FromResult(Result<EUnit, NetworkFailure>.Ok(EUnit.Value));
                },
                allowDuplicates: true,
                token: cancellationToken
            ).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                Log.Error("[SETTINGS-VM] Failed to load profile: {Error}", networkResult.UnwrapErr().Message);
                return;
            }

            GetAccountProfileResponse response = await responseSource.Task.ConfigureAwait(false);

            if (response.Profile != null)
            {
                Guid accId = Helpers.FromByteStringToGuid(response.Profile.AccountId);
                Guid profId = Helpers.FromByteStringToGuid(response.Profile.ProfileId);

                DisplayName = response.Profile.DisplayName;
                ProfileName = response.Profile.ProfileName;
                AccountId = accId.ToString();
                ProfileId = profId.ToString();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SETTINGS-VM] Unexpected error loading profile");
        }
        finally
        {
            IsLoadingProfile = false;
            _isInternalUpdate = false;
        }
    }

    private async Task<Option<Guid>> GetCurrentAccountIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _secureStorage.GetApplicationInstanceSettingsAsync();

        if (settingsResult.IsOk)
        {
            Guid accountId = Helpers.FromByteStringToGuid(settingsResult.Unwrap().CurrentAccountId);
            return Option<Guid>.Some(accountId);
        }

        Log.Warning("[CHAT-VM] Cannot load the account id from secure storage: {Error}",
            settingsResult.UnwrapErr().Message);
        return Option<Guid>.None;
    }

    private static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        string[] parts = displayName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0].Substring(0, 2).ToUpper()
                : parts[0].ToUpper();
        }

        string initials = $"{parts[0][0]}{parts[^1][0]}";
        return initials.ToUpper();
    }
}
