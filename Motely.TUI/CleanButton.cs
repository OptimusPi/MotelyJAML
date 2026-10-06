using Terminal.Gui;

namespace Motely.TUI;

/// <summary>
/// Clean button without Terminal.Gui's ugly default brackets.
/// Single-line button with solid colored background and centered text.
/// Drop-in replacement for Button.
/// </summary>
public class CleanButton : View
{
    public event EventHandler<EventArgs>? Accept;
    private Alignment _textAlignment = Alignment.Center;
    private string _text = "";
    private bool _widthSet = false;

    public CleanButton()
    {
        Height = 1;
        CanFocus = true;

        DrawingContent += (s, e) => DrawContent();

        AddCommand(
            Command.Accept,
            () =>
            {
                SetFocus();
                Accept?.Invoke(this, EventArgs.Empty);
                return true;
            }
        );
        KeyBindings.Clear();
        KeyBindings.Add(KeyCode.Enter, Command.Accept);
        KeyBindings.Add(KeyCode.Space, Command.Accept);
        MouseBindings.Clear();
        MouseBindings.Add(MouseFlags.Button1Clicked, Command.Accept);
    }

    // Track when Width is explicitly set
    public new Dim? Width
    {
        get => base.Width;
        set
        {
            if (value != null)
            {
                _widthSet = true;
                base.Width = value;
            }
        }
    }

    // Auto-size width based on text if no explicit width set
    public new string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            // Auto-size width only if not explicitly set
            if (!_widthSet)
            {
                base.Width = _text.Length + 2; // Add padding
            }
        }
    }

    public new Alignment TextAlignment
    {
        get => _textAlignment;
        set => _textAlignment = value;
    }

    private void DrawContent()
    {
        var viewport = Viewport;
        var attr = HasFocus ? GetFocusColor() : GetNormalColor();
        SetAttribute(attr);

        // Fill entire button area
        for (int y = 0; y < viewport.Height; y++)
        {
            for (int x = 0; x < viewport.Width; x++)
            {
                AddRune(x, y, (System.Text.Rune)' ');
            }
        }

        // Draw text
        string text = _text;
        int textY = viewport.Height / 2;
        int textX;

        switch (_textAlignment)
        {
            case Alignment.Start:
                textX = 1;
                break;
            case Alignment.End:
                textX = viewport.Width - text.Length - 1;
                break;
            default: // Center
                textX = (viewport.Width - text.Length) / 2;
                break;
        }

        if (textX < 0)
            textX = 0;

        for (int i = 0; i < text.Length && textX + i < viewport.Width; i++)
        {
            AddRune(textX + i, textY, (System.Text.Rune)text[i]);
        }
    }
}
