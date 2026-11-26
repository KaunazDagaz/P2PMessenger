using P2PMessenger.Core.Crypto;
using P2PMessenger.Core.Groups;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

internal sealed record RelayedGroupAcknowledgement(GroupAcknowledgement Acknowledgement, Ed25519PublicKey AuthorKey, long Sequence)
{
    public GroupId GroupId => Acknowledgement.GroupId;

    public MessageId MessageId => Acknowledgement.MessageId;

    public Ed25519PublicKey RecipientKey => Acknowledgement.RecipientKey;

    public MessageId NoticeId => Acknowledgement.NoticeId;
}
