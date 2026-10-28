using FolderChat.Core.Knowledge;

namespace FolderChat.Core.Conversations;

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Title { get; set; }

    /// <summary>The provider used most recently, so reopening the chat selects the same model.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>The model used most recently.</summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// The knowledge folders this chat answers from, chosen for this chat alone. A removed folder's ID can linger here;
    /// it is ignored, like a removed provider.
    /// </summary>
    public List<Guid> FolderIds { get; set; } = [];

    /// <summary>How hard this chat searches its folders.</summary>
    public SearchEffort Effort { get; set; } = SearchEffort.Standard;

    /// <summary>Where the conversation was started: the chat page, or another app through the API.</summary>
    public ConversationSource Source { get; set; } = ConversationSource.App;

    /// <summary>For API conversations, the user ID the calling app sent, so it can tell its users' chats apart.</summary>
    public string? ExternalUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];
}
