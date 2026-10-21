namespace Motely.TUI;

/// <summary>
/// Mimick the Balatro main menu "button Dock"-- sort of :)
/// Supports hotkeys with underscore notation (e.g., "_SEARCH" makes S the hotkey)
/// </summary>
public class MenuButton : View
{
    public event EventHandler<EventArgs>? Accept;
    private bool _useHalfBlock = false;
    private bool _dynamicFocusHeight = false;
    private Color _dockBackground = BalatroTheme.ModalGrey;
    private string _displayText = "";
    private int _hotKeyIndex = -1; // Index of hotkey char in display text (-1 = none)

    /// <summary>
    /// When true, button appears shorter (half-block) when unfocused and full height when focused.
    /// This provides visual feedback for TAB navigation.
    /// </summary>
    public bool DynamicFocusHeight
    {
        get => _dynamicFocusHeight;
        set => _dynamicFocusHeight = value;
    }

    /// <summary>
    /// The background color of the dock/container (shown in half-block top row).
    /// </summary>
    public Color DockBackground
    {
        get => _dockBackground;
        set => _dockBackground = value;
    }

    public MenuButton(string text, ColorScheme colorScheme, bool useHalfBlock = false)
    {
        Width = 16;
        Height = 3;
        CanFocus = true;
        TabStop = TabBehavior.TabStop; // Required for TAB navigation in v2
        ColorScheme = colorScheme;
        _useHalfBlock = useHalfBlock;

        // Parse hotkey from text (underscore notation)
        ParseHotKey(text);

        AddCommand(
            Command.Accept,
            () =>
            {
                SetFocus();
                Accept?.Invoke(this, EventArgs.Empty);
                return true;
            }
        );

        // Clear default bindings that might conflict, then add ours
        KeyBindings.Clear();
        KeyBindings.Add(KeyCode.Enter, Command.Accept);
        KeyBindings.Add(KeyCode.Space, Command.Accept);

        MouseBindings.Clear();
        MouseBindings.Add(MouseFlags.Button1Clicked, Command.Accept);

        DrawingContent += (s, e) => DrawContent();

        // Force redraw when focus changes (for DynamicFocusHeight visual effect)
        // Also redraw parent so siblings update correctly
        HasFocusChanged += (s, e) =>
        {
            SetNeedsDraw();
            SuperView?.SetNeedsDraw();
        };
    }

    /// <summary>
    /// Parse text for hotkey (underscore notation like "_SEARCH" or "E_XIT")
    /// </summary>
    private void ParseHotKey(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _displayText = "";
            _hotKeyIndex = -1;
            Text = "";
            return;
        }

        int underscoreIndex = text.IndexOf('_');
        if (underscoreIndex >= 0 && underscoreIndex < text.Length - 1)
        {
            // Found underscore - next char is the hotkey
            _displayText = text.Remove(underscoreIndex, 1); // Remove the underscore
            _hotKeyIndex = underscoreIndex; // Index of hotkey char in display text
            // Visual hotkey indicator only - actual hotkey binding handled by parent
        }
        else
        {
            _displayText = text;
            _hotKeyIndex = -1;
        }

        Text = _displayText;
    }

    private void DrawContent()
    {
        var viewport = Viewport;
        var attr = HasFocus ? GetFocusColor() : GetNormalColor();
        var buttonColor = attr.Background;

        // Determine if we should draw half-block (shorter) top row
        // With DynamicFocusHeight: half-block when NOT focused, full when focused
        // With static _useHalfBlock: always half-block if set
        bool drawHalfBlock = _dynamicFocusHeight ? !HasFocus : _useHalfBlock;

        // Draw half-block TOP row if enabled (shaves off top, bottoms align)
        int startY = 0;
        if (drawHalfBlock && viewport.Height > 0)
        {
            // Lower half block on row 0: foreground = button color (bottom half), background = dock color (top half)
            var halfAttr = new Attribute(buttonColor, _dockBackground);
            SetAttribute(halfAttr);
            for (int x = 0; x < viewport.Width; x++)
            {
                AddRune(x, 0, (System.Text.Rune)'▄'); // Lower half block
            }
            startY = 1;
        }

        // Draw solid button rows
        SetAttribute(attr);
        for (int y = startY; y < viewport.Height; y++)
        {
            for (int x = 0; x < viewport.Width; x++)
            {
                AddRune(x, y, (System.Text.Rune)' ');
            }
        }

        // Draw centered text (in the solid area) with hotkey underlined
        string text = _displayText;
        int solidHeight = viewport.Height - startY;
        // For half-block buttons, put text on first solid row (closer to top)
        // For normal buttons, center vertically
        int textY = drawHalfBlock ? startY : startY + (solidHeight / 2);
        int textX = (viewport.Width - text.Length) / 2;
        if (textX < 0)
            textX = 0;

        // Create underline attribute for hotkey character
        var hotKeyAttr = new Attribute(attr.Foreground, attr.Background);

        for (int i = 0; i < text.Length; i++)
        {
            if (i == _hotKeyIndex)
            {
                // Draw hotkey character with underline (using combining character)
                SetAttribute(hotKeyAttr);
                AddRune(textX + i, textY, (System.Text.Rune)text[i]);
                // Draw underline on row below if we have space, otherwise use combining underline
                if (textY + 1 < viewport.Height)
                {
                    SetAttribute(attr);
                    AddRune(textX + i, textY + 1, (System.Text.Rune)'̲'); // Combining underline (may not render well)
                }
            }
            else
            {
                SetAttribute(attr);
                AddRune(textX + i, textY, (System.Text.Rune)text[i]);
            }
        }

        // Draw a subtle underline bar under the hotkey character on the next row
        if (_hotKeyIndex >= 0 && textY + 1 < viewport.Height)
        {
            var underlineAttr = new Attribute(BalatroTheme.White, attr.Background);
            SetAttribute(underlineAttr);
            AddRune(textX + _hotKeyIndex, textY + 1, (System.Text.Rune)'‾'); // Overline as underline visually
        }
    }
}
