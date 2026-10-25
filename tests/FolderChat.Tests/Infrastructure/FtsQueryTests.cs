using FolderChat.Infrastructure.Knowledge;

namespace FolderChat.Tests.Infrastructure;

public sealed class FtsQueryTests
{
    [Theory]
    [InlineData("How much did we spend on hosting last year?", "\"much\" OR \"spend\" OR \"hosting\" OR \"last\" OR \"year\"")]
    [InlineData("invoice #4521 (Acme) NOT \"urgent\"*", "\"invoice\" OR \"4521\" OR \"acme\" OR \"urgent\"")]
    [InlineData("Who are we?", "\"who\" OR \"are\" OR \"we\"")]
    [InlineData("Café crème, café", "\"café\" OR \"crème\"")]
    [InlineData("Q3 vs. Q4", "\"q3\" OR \"vs\" OR \"q4\"")]
    [InlineData("?!", null)]
    public void Questions_become_queries_of_their_meaningful_words(string text, string? expected) =>
        Assert.Equal(expected, FtsQuery.Build(text));
}
