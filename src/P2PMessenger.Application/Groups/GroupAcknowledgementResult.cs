namespace P2PMessenger.Application.Groups;

internal readonly record struct GroupAcknowledgementResult(bool ReplyWithNoticeAck, bool MarksSenderHasMessage);
