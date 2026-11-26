using P2PMessenger.Application.Chats;
using P2PMessenger.Application.Contacts;
using P2PMessenger.Application.Copy;
using P2PMessenger.Application.Groups;
using P2PMessenger.Application.Identity;
using P2PMessenger.Core.Messages;

namespace P2PMessenger.Application.LocalData;

public sealed record LocalDataStores(
    IIdentityStore Identity,
    IContactStore Contacts,
    IChatStore Chats,
    IGroupStore Groups,
    ISeenMessageIds SeenMessageIds,
    ICopiedChatStore CopiedChats,
    LocalDataMode Mode);
