using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Knowledge;

/// <summary>How much one reply may search and read at each effort level. More effort means better answers, slower and with more tokens.</summary>
/// <param name="Searches">Searches per question.</param>
/// <param name="PassagesPerSearch">Passages each search returns.</param>
/// <param name="Reads">Pages or parts of files the model may read whole.</param>
/// <param name="Lists">Times the model may look at the list of files.</param>
/// <param name="TextBudget">Characters of folder text sent to the model per question, at most.</param>
internal sealed record EffortLimits(int Searches, int PassagesPerSearch, int Reads, int Lists, int TextBudget)
{
    public static EffortLimits For(SearchEffort effort) => effort switch
    {
        SearchEffort.Quick => new(Searches: 1, PassagesPerSearch: 8, Reads: 0, Lists: 0, TextBudget: 12_000),
        SearchEffort.Thorough => new(Searches: 10, PassagesPerSearch: 8, Reads: 8, Lists: 3, TextBudget: 120_000),
        _ => new(Searches: 4, PassagesPerSearch: 6, Reads: 0, Lists: 0, TextBudget: 30_000),
    };

    /// <summary>Model round trips: one per tool call the model may make, plus one to answer and one to spare.</summary>
    public int MaxRounds => Searches + Reads + Lists + 2;
}
