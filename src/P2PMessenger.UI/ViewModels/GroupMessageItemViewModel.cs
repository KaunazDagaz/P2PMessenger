using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Groups;
using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Messages;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class GroupMessageItemViewModel : ViewModelBase
{
    private readonly Func<GroupMessageItemViewModel, CancellationToken, Task>? delete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanDelete), nameof(IsDeleteVisible))]
    private int receivedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteVisible))]
    private bool isDetailsShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteVisible))]
    private bool isConfirmingDelete;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand), nameof(CancelDeleteCommand))]
    private bool isDeleting;

    public GroupMessageItemViewModel(GroupChatMessage message, string sender, TimeZoneInfo timeZone)
        : this(message, sender, timeZone, null, null)
    {
    }

    public GroupMessageItemViewModel(
        GroupChatMessage message,
        string sender,
        TimeZoneInfo timeZone,
        Func<GroupMessageItemViewModel, CancellationToken, Task>? delete,
        Func<Ed25519PublicKey, string>? memberName = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(timeZone);

        Id = message.Id;
        IsOwn = message.Direction == MessageDirection.Outgoing;
        Sender = sender;
        WrittenAt = TimeZoneInfo.ConvertTime(message.WrittenAt, timeZone).ToString("g", CultureInfo.CurrentCulture);
        Text = message.Text;
        RecipientCount = message.RecipientCount;
        ReceivedCount = message.ReceivedCount;
        this.delete = delete;
        SyncMemberDeliveries(message, memberName);
    }

    public MessageId Id { get; }

    public bool IsOwn { get; }

    public bool IsTheirs => !IsOwn;

    public string Sender { get; }

    public string WrittenAt { get; }

    public string Text { get; }

    public int RecipientCount { get; }

    public ObservableCollection<GroupDeliveryMemberViewModel> MemberDeliveries { get; } = [];

    public string? StateText => !IsOwn ? null
        : ReceivedCount == 0 ? UiText.DeliveryWaiting
        : Formats.WithAll(Formats.GroupDeliveryReceived, ReceivedCount, RecipientCount);

    public bool CanDelete => delete is not null && IsOwn && RecipientCount > 0 && ReceivedCount == 0;

    public bool IsDeleteVisible => CanDelete && IsDetailsShown && !IsConfirmingDelete;

    private bool IsIdle => !IsDeleting;

    internal void Update(GroupChatMessage message, Func<Ed25519PublicKey, string>? memberName = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        ReceivedCount = message.ReceivedCount;
        SyncMemberDeliveries(message, memberName);
        if (!CanDelete)
        {
            IsConfirmingDelete = false;
        }
    }

    private void SyncMemberDeliveries(GroupChatMessage message, Func<Ed25519PublicKey, string>? memberName)
    {
        if (!IsOwn)
        {
            return;
        }

        List<(Ed25519PublicKey Key, string Name, DeliveryState State)> ordered =
        [
            .. message.Deliveries
                .Select(pair => (Key: pair.Key, Name: memberName?.Invoke(pair.Key) ?? string.Empty, State: pair.Value))
                .OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static item => item.Name, StringComparer.Ordinal)
                .ThenBy(static item => item.Key, KeyOrder.Instance),
        ];

        for (int index = 0; index < ordered.Count; index++)
        {
            (Ed25519PublicKey key, string name, DeliveryState state) = ordered[index];
            if (index < MemberDeliveries.Count && MemberDeliveries[index].IdentityKey.Equals(key))
            {
                MemberDeliveries[index].Name = name;
                MemberDeliveries[index].IsReceived = state == DeliveryState.Received;
                continue;
            }

            int existingIndex = -1;
            for (int search = index + 1; search < MemberDeliveries.Count; search++)
            {
                if (MemberDeliveries[search].IdentityKey.Equals(key))
                {
                    existingIndex = search;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                GroupDeliveryMemberViewModel existing = MemberDeliveries[existingIndex];
                existing.Name = name;
                existing.IsReceived = state == DeliveryState.Received;
                MemberDeliveries.Move(existingIndex, index);
            }
            else
            {
                MemberDeliveries.Insert(index, new GroupDeliveryMemberViewModel(key, name, state));
            }
        }

        while (MemberDeliveries.Count > ordered.Count)
        {
            MemberDeliveries.RemoveAt(MemberDeliveries.Count - 1);
        }
    }

    private sealed class KeyOrder : IComparer<Ed25519PublicKey>
    {
        public static KeyOrder Instance { get; } = new();

        public int Compare(Ed25519PublicKey? x, Ed25519PublicKey? y) =>
            x is null ? (y is null ? 0 : -1) : y is null ? 1 : x.Bytes.SequenceCompareTo(y.Bytes);
    }

    [RelayCommand]
    private void ToggleDetails() => IsDetailsShown = !IsDetailsShown;

    [RelayCommand]
    private void AskDelete()
    {
        if (CanDelete)
        {
            IsConfirmingDelete = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmDeleteAsync(CancellationToken cancellationToken)
    {
        if (delete is null || !CanDelete)
        {
            IsConfirmingDelete = false;
            return;
        }

        IsDeleting = true;
        try
        {
            await delete(this, cancellationToken);
        }
        finally
        {
            IsDeleting = false;
            IsConfirmingDelete = false;
        }
    }
}
