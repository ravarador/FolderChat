using FolderChat.Core.Conversations;

namespace FolderChat.Application.Abstractions;

/// <summary>Loads and saves conversations. Entities it returns are tracked, so edits persist on <see cref="SaveChangesAsync"/>.</summary>
public interface IConversationStore
{
    /// <summary>All conversations without their messages, most recently updated first.</summary>
    Task<List<Conversation>> ListAsync(CancellationToken ct = default);

    /// <summary>Finds a conversation. With <paramref name="includeMessages"/>, its messages come back oldest first.</summary>
    Task<Conversation?> FindAsync(Guid id, bool includeMessages, CancellationToken ct = default);

    void Add(Conversation conversation);

    void AddMessage(ConversationMessage message);

    void Remove(Conversation conversation);

    Task SaveChangesAsync(CancellationToken ct = default);
}
