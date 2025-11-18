using System;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Unit = System.Reactive.Unit;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class FoundUserViewModel
{
    [Reactive] public string Nickname { get; set; } = string.Empty;
    [Reactive] public string PhoneNumber { get; set; } = string.Empty;
    [Reactive] public string Initials { get; set; } = string.Empty;
}

public sealed partial class ChatsViewModel : Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;
    private readonly IApplicationSecureStorageProvider _secureStorage;

    [Reactive] public string Title { get; set; }
    public ObservableCollection<string> Conversations { get; }

    [Reactive] public string PhoneNumberInput { get; set; } = string.Empty;

    [Reactive] public FoundUserViewModel? FoundUser { get; set; }

    [Reactive] public bool IsLoading { get; set; }

    [Reactive] public string? ErrorMessage { get; set; }

    public ReactiveCommand<Unit, Unit> FindCommand { get; }

    public ChatsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider secureStorageProvider)
        : base(networkProvider, localizationService, null)
    {
        _secureStorage = secureStorageProvider;

        Title = "Your Chats";
        Conversations = new ObservableCollection<string>
        {
            "No conversations yet..."
        };

        IObservable<bool> canFind = this.WhenAnyValue(
            x => x.PhoneNumberInput,
            (phone) => !string.IsNullOrWhiteSpace(phone));

        FindCommand = ReactiveCommand.CreateFromTask(ExecuteFindAsync, canFind);
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

    private async Task ExecuteFindAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        FoundUser = null;
        ErrorMessage = null;

        try
        {
            Option<Guid> accountIdOpt = await GetCurrentAccountIdAsync();

            if (!accountIdOpt.IsSome)
            {
                ErrorMessage = "Current user session is invalid.";
                return;
            }

            Guid currentAccountId = accountIdOpt.Value;

            GetAccountProfileByMobileRequest request = new()
            {
                MobileNumber = PhoneNumberInput,
                AccountId = Helpers.GuidToByteString(currentAccountId)
            };

            TaskCompletionSource<GetAccountProfileByMobileResponse> responseSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);


            Result<EUnit, NetworkFailure> networkResult = await NetworkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.GetUserInfoByMobileNumber,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    GetAccountProfileByMobileResponse response = Helpers.ParseFromBytes<GetAccountProfileByMobileResponse>(payload);
                    responseSource.TrySetResult(response);
                    return Task.FromResult(Result<EUnit, NetworkFailure>.Ok(EUnit.Value));
                },
                allowDuplicates: true,
                token: cancellationToken
            ).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                ErrorMessage = networkResult.UnwrapErr().Message;
                return;
            }

            GetAccountProfileByMobileResponse response = await responseSource.Task.ConfigureAwait(false);

            if (response.Profile != null)
            {
                FoundUser = new FoundUserViewModel
                {
                    Nickname = response.Profile.DisplayName,
                    PhoneNumber = PhoneNumberInput,
                    Initials = GetInitials(response.Profile.DisplayName)
                };
            }

        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            FoundUser = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        string[] parts = displayName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            return parts[0].Substring(0, Math.Min(parts[0].Length, 2)).ToUpper();
        }

        string initials = $"{parts[0][0]}{parts[^1][0]}";

        return initials.ToUpper();
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
