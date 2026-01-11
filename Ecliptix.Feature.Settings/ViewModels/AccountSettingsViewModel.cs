using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Account;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using EUnit = Ecliptix.Utilities.Unit;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Settings.ViewModels;

public class AccountSettingsViewModel : Ecliptix.Core.MVVM.ViewModelBase, IActivatableViewModel
{
    private readonly IApplicationSecureStorageProvider _secureStorage;

    private bool _isInternalUpdate;

    [Reactive] public string DisplayName { get; set; } = string.Empty;
    [Reactive] public string ProfileName { get; set; } = string.Empty;
    [Reactive] public string AccountId { get; set; } = string.Empty;
    [Reactive] public string ProfileId { get; set; } = string.Empty;
    [Reactive] public string ProfileInitials { get; set; } = string.Empty;
    [Reactive] public bool IsLoadingProfile { get; set; }

    [Reactive] public bool IsSavedMessageVisible { get; set; }
    public ReactiveCommand<SystemU, SystemU> SaveChangesCommand { get; private set; }

    public ReactiveCommand<SystemU, SystemU> ChangeAvatarCommand { get; private set; }

    public new ViewModelActivator Activator { get; } = new();

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

            ByteString accountId = ByteString.CopyFrom(currentAccountId.ToByteArray());
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
                request.ToByteArray(),
                payload =>
                {
                    GetAccountProfileResponse response = GetAccountProfileResponse.Parser.ParseFrom(payload);
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
                Guid accId = new(response.Profile.AccountId.ToByteArray());
                Guid profId = new(response.Profile.ProfileId.ToByteArray());

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
            ApplicationInstanceSettings settings = settingsResult.Unwrap();
            if (settings.CurrentAccountId is { IsEmpty: false })
            {
                Guid accountId = new(settings.CurrentAccountId.ToByteArray());
                return Option<Guid>.Some(accountId);
            }
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
