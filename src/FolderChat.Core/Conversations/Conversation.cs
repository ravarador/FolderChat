namespace FolderChat.Core.Conversations;

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Title { get; set; }

    /// <summary>The provider used most recently, so reopening the chat selects the same model.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>The model used most recently.</summary>
    public string? ModelId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];
}
