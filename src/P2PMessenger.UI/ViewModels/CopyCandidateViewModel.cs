using CommunityToolkit.Mvvm.ComponentModel;
using P2PMessenger.Application.Copy;
using P2PMessenger.Core.Copy;
using P2PMessenger.UI.Resources;

namespace P2PMessenger.UI.ViewModels;

public sealed partial class CopyCandidateViewModel : ViewModelBase
{
    private readonly Action selectionChanged;

    [ObservableProperty]
    private bool isSelected;

    public CopyCandidateViewModel(CopyCandidate candidate, Action selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selectionChanged);

        this.selectionChanged = selectionChanged;
        Origin = candidate.Origin;
        Title = candidate.Title.Value;
        IsGroup = candidate.IsGroup;
        MessageCount = candidate.MessageCount;
        TotalMessageCount = candidate.TotalMessageCount;
        IsTruncated = candidate.IsTruncated;
        DetailText = candidate.IsTruncated
            ? Formats.WithAll(
                candidate.IsGroup ? Formats.SendCopyCandidateGroupTruncated : Formats.SendCopyCandidateContactTruncated,
                candidate.MessageCount,
                candidate.TotalMessageCount)
            : candidate.IsGroup
                ? candidate.MessageCount == 1 ? UiText.SendCopyCandidateGroupOne : Formats.With(Formats.SendCopyCandidateGroup, candidate.MessageCount)
                : candidate.MessageCount == 1 ? UiText.SendCopyCandidateContactOne : Formats.With(Formats.SendCopyCandidateContact, candidate.MessageCount);
    }

    public CopyOriginId Origin { get; }

    public string Title { get; }

    public bool IsGroup { get; }

    public int MessageCount { get; }

    public int TotalMessageCount { get; }

    public bool IsTruncated { get; }

    public string DetailText { get; }

    partial void OnIsSelectedChanged(bool value) => selectionChanged();
}
