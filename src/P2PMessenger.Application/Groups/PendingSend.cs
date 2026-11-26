using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.Groups;

internal readonly record struct PendingSend(MessageId Id, SessionMessage Message);
