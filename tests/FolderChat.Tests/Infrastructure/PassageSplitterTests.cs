using FolderChat.Infrastructure.Knowledge;

namespace FolderChat.Tests.Infrastructure;

public sealed class PassageSplitterTests
{
    [Fact]
    public void Short_paragraphs_are_gathered_into_one_passage()
    {
        var passage = Assert.Single(Split("First paragraph.\n\n  \nSecond paragraph,\nsame paragraph."));

        Assert.Equal("First paragraph.\n\nSecond paragraph,\nsame paragraph.", passage.Text);
        Assert.Equal((1, (int?)null, 0), (passage.Ordinal, passage.Page, passage.StartOffset));
    }

    [Fact]
    public void Passages_close_once_they_reach_the_target_length()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("word", 100)); // 499 characters
        var text = string.Join("\n\n", Enumerable.Repeat(paragraph, 6));

        var passages = Split(text);

        // Two paragraphs come to 1,000 characters, under the target; the third takes a passage past it.
        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal((3 * paragraph.Length) + 4, p.Text.Length));
        Assert.Equal(3 * (paragraph.Length + 2), passages[1].StartOffset);
    }

    [Fact]
    public void Long_paragraphs_are_cut_at_sentence_ends()
    {
        var paragraph = string.Concat(Enumerable.Repeat("This sentence is here to make a long paragraph. ", 70)).TrimEnd();

        var passages = Split(paragraph);

        Assert.True(passages.Count > 1);
        Assert.All(passages, p => Assert.True(p.Text.Length <= PassageSplitter.MaxLength));
        Assert.All(passages, p => Assert.EndsWith(".", p.Text));
        Assert.Equal(paragraph, string.Join(' ', passages.Select(p => p.Text)));
        Assert.All(passages, p => Assert.Equal(p.Text, paragraph.Substring(p.StartOffset, p.Text.Length)));
    }

    [Fact]
    public void Pdf_pages_become_page_numbers_and_passages_never_cross_them()
    {
        const string text = "[Page 1]\nIntro text.\n\n[Page 3]\nLater text.";

        var passages = PassageSplitter.Split(text, paged: true).ToList();

        Assert.Equal([(1, (int?)1, "Intro text."), (2, 3, "Later text.")], passages.Select(p => (p.Ordinal, p.Page, p.Text)));
        Assert.Equal(text.IndexOf("Later", StringComparison.Ordinal), passages[1].StartOffset);
    }

    [Fact]
    public void Page_markers_in_other_files_are_just_text()
    {
        var passage = Assert.Single(Split("[Page 1]\nNot a PDF."));

        Assert.Null(passage.Page);
        Assert.Equal("[Page 1]\nNot a PDF.", passage.Text);
    }

    private static List<SplitPassage> Split(string text) => PassageSplitter.Split(text, paged: false).ToList();
}
