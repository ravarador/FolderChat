namespace FolderChat.Core.Knowledge;

/// <summary>How hard a chat works to find an answer in a knowledge folder.</summary>
public enum SearchEffort
{
    /// <summary>FolderChat searches once with the question and sends the best passages. Works with every model.</summary>
    Quick = 1,

    /// <summary>The model searches the folder itself, trying other words if needed, for a few rounds.</summary>
    Standard = 2,

    /// <summary>More rounds of searching, and the model can read whole pages and see the list of files.</summary>
    Thorough = 3,
}
