using System.Text;

namespace FolderChat.Infrastructure.Documents;

/// <summary>Collects text up to a length limit and remembers whether anything was cut off.</summary>
internal sealed class BoundedText(int maxLength)
{
    private readonly StringBuilder text = new();

    /// <summary>True once text was dropped to stay under the limit. Nothing more is added after that.</summary>
    public bool IsTruncated { get; private set; }

    /// <summary>Adds a paragraph, table or page, after a blank line.</summary>
    public void AppendBlock(string block)
    {
        if (text.Length > 0)
        {
            Append("\n\n");
        }

        Append(block);
    }

    /// <summary>Adds a line, such as the next item of a list, after a single line break.</summary>
    public void AppendLine(string line)
    {
        if (text.Length > 0)
        {
            Append("\n");
        }

        Append(line);
    }

    public void Append(string value)
    {
        if (IsTruncated || value.Length == 0)
        {
            return;
        }

        var room = maxLength - text.Length;
        if (value.Length <= room)
        {
            text.Append(value);
            return;
        }

        // Don't split a character that takes two UTF-16 code units, such as an emoji.
        if (room > 0 && char.IsHighSurrogate(value[room - 1]))
        {
            room--;
        }

        text.Append(value, 0, room);
        IsTruncated = true;
    }

    public override string ToString() => text.ToString();
}
