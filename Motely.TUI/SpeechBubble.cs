namespace Motely.TUI;

/// <summary>
/// A speech bubble view that displays text with a pointer tail.
/// The tail points to the right (toward Jimbo).
/// </summary>
public class SpeechBubble : View
{
    private string _message = "";
    private const int PADDING = 1;
    private const int TAIL_WIDTH = 3;

    public string Message
    {
        get => _message;
        set
        {
            _message = value ?? "";
            SetNeedsDraw();
        }
    }

    public SpeechBubble()
    {
        CanFocus = false;
        Height = 5; // Top border + padding + text + padding + bottom border with tail
        DrawingContent += (s, e) => DrawContent();
    }

    private void DrawContent()
    {
        var viewport = Viewport;

        if (string.IsNullOrEmpty(_message) || viewport.Width < 5 || viewport.Height < 3)
            return;

        var attr = new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey);
        var borderAttr = new Attribute(BalatroTheme.LightGrey, BalatroTheme.DarkGrey);

        // Calculate text area
        int textAreaWidth = viewport.Width - 2; // -2 for left and right borders

        // Draw top border: ╭────────────────────────╮
        SetAttribute(borderAttr);
        AddRune(0, 0, (System.Text.Rune)'╭');
        for (int x = 1; x < viewport.Width - 1; x++)
            AddRune(x, 0, (System.Text.Rune)'─');
        AddRune(viewport.Width - 1, 0, (System.Text.Rune)'╮');

        // Draw middle rows (content area)
        for (int y = 1; y < viewport.Height - 1; y++)
        {
            SetAttribute(borderAttr);
            AddRune(0, y, (System.Text.Rune)'│');

            // Fill interior with background
            SetAttribute(attr);
            for (int x = 1; x < viewport.Width - 1; x++)
                AddRune(x, y, (System.Text.Rune)' ');

            SetAttribute(borderAttr);
            AddRune(viewport.Width - 1, y, (System.Text.Rune)'│');
        }

        // Draw bottom border with tail pointing right: ╰─────────────────◄─╯
        SetAttribute(borderAttr);
        AddRune(0, viewport.Height - 1, (System.Text.Rune)'╰');

        int tailPos = viewport.Width - TAIL_WIDTH - 1;
        for (int x = 1; x < viewport.Width - 1; x++)
        {
            if (x == tailPos)
            {
                // Draw tail pointer
                SetAttribute(new Attribute(BalatroTheme.White, BalatroTheme.DarkGrey));
                AddRune(x, viewport.Height - 1, (System.Text.Rune)'◄');
            }
            else if (x == tailPos + 1)
            {
                AddRune(x, viewport.Height - 1, (System.Text.Rune)'─');
            }
            else
            {
                AddRune(x, viewport.Height - 1, (System.Text.Rune)'─');
            }
        }
        AddRune(viewport.Width - 1, viewport.Height - 1, (System.Text.Rune)'╯');

        // Draw the message text (word-wrapped and centered)
        SetAttribute(attr);
        var lines = WrapText(_message, textAreaWidth);
        int contentHeight = viewport.Height - 2; // Minus top and bottom borders
        int startTextY = 1 + Math.Max(0, (contentHeight - lines.Count) / 2);

        for (int i = 0; i < lines.Count && startTextY + i < viewport.Height - 1; i++)
        {
            string line = lines[i];
            int lineX = 1 + (textAreaWidth - line.Length) / 2;
            if (lineX < 1)
                lineX = 1;

            for (int j = 0; j < line.Length && lineX + j < viewport.Width - 1; j++)
            {
                AddRune(lineX + j, startTextY + i, (System.Text.Rune)line[j]);
            }
        }
    }

    /// <summary>
    /// Word-wrap text to fit within the specified width.
    /// </summary>
    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            return lines;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length == 0)
            {
                // First word on line
                if (word.Length > maxWidth)
                {
                    // Word too long, truncate
                    lines.Add(word.Substring(0, maxWidth - 3) + "...");
                }
                else
                {
                    currentLine.Append(word);
                }
            }
            else if (currentLine.Length + 1 + word.Length <= maxWidth)
            {
                // Word fits on current line
                currentLine.Append(' ');
                currentLine.Append(word);
            }
            else
            {
                // Start new line
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                if (word.Length > maxWidth)
                {
                    lines.Add(word.Substring(0, maxWidth - 3) + "...");
                }
                else
                {
                    currentLine.Append(word);
                }
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines;
    }
}
