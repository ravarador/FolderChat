namespace FolderChat.Core.Conversations;

public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }

    /// <summary>Position in the conversation, starting at 1. Messages are always shown and replayed in this order.</summary>
    public int Sequence { get; set; }

    public MessageRole Role { get; set; }

    public required string Content { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Complete;

    /// <summary>What went wrong, for <see cref="MessageStatus.Failed"/> messages.</summary>
    public string? Error { get; set; }

    /// <summary>The provider that wrote an assistant message.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>A copy of the provider's name, so history still reads well after the provider is deleted.</summary>
    public string? ProviderName { get; set; }

    /// <summary>The model that wrote an assistant message.</summary>
    public string? ModelId { get; set; }

    /// <summary>Tokens the model read, summed over every round of a reply that searched folders.</summary>
    public long? InputTokens { get; set; }

    /// <summary>Tokens the model wrote, summed over every round.</summary>
    public long? OutputTokens { get; set; }

    /// <summary>The part of <see cref="InputTokens"/> the provider read from its prompt cache, when it says.</summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>The part of <see cref="OutputTokens"/> the model spent thinking, when the provider says.</summary>
    public long? ReasoningTokens { get; set; }

    /// <summary>How much of the model's context the last round used: its input and output tokens together.</summary>
    public long? ContextTokens { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Files sent with a user message, in <see cref="Attachment.Position"/> order.</summary>
    public List<Attachment> Attachments { get; set; } = [];

    /// <summary>Folder passages an assistant message was given, in <see cref="MessageSource.Number"/> order.</summary>
    public List<MessageSource> Sources { get; set; } = [];
}

public enum MessageRole
{
    User = 1,
    Assistant = 2,
}

public enum MessageStatus
{
    Complete = 1,

    /// <summary>The user pressed Stop. <c>Content</c> holds what arrived before that.</summary>
    Stopped = 2,

    /// <summary>The provider returned an error.</summary>
    Failed = 3,
}
