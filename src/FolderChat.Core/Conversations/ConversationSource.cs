namespace FolderChat.Core.Conversations;

/// <summary>Where a conversation was started.</summary>
public enum ConversationSource
{
    /// <summary>In FolderChat's own chat page.</summary>
    App = 1,

    /// <summary>By another app, through the REST API at <c>/api/v1</c>.</summary>
    Api = 2,
}
