namespace Motely.TUI;

public static class BalatroTheme
{
    // Button colors
    public static readonly Color Blue = new(66, 135, 245);
    public static readonly Color DarkBlue = new(45, 95, 180);
    public static readonly Color Orange = new(255, 165, 0);
    public static readonly Color DarkOrange = new(200, 120, 0);
    public static readonly Color Red = new(255, 95, 95);
    public static readonly Color DarkRed = new(180, 60, 60);
    public static readonly Color Green = new(76, 160, 100);
    public static readonly Color DarkGreen = new(50, 115, 70);
    public static readonly Color Purple = new(125, 96, 224);
    public static readonly Color DarkPurple = new(90, 65, 165);
    public static readonly Color SlateGray = new(85, 95, 110);

    // UI colors
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color ModalGrey = new(55, 66, 77);
    public static readonly Color DarkGrey = new(35, 42, 52);
    public static readonly Color InnerPanelGrey = new(45, 54, 64);
    public static readonly Color LightGrey = new(180, 185, 195);
    public static readonly Color MediumGrey = new(100, 110, 120);
    public static readonly Color BrightSilver = new(200, 205, 215);
    public static readonly Color Gray = new(128, 128, 128);

    // Red modal button (hover = DarkRed)
    public static Scheme ModalButton => new()
    {
        Normal = new Attribute(White, Red),
        Focus = new Attribute(White, DarkRed),
        HotNormal = new Attribute(White, Red),
        HotFocus = new Attribute(White, DarkRed),
    };

    // Orange Back button (hover = DarkOrange)
    public static Scheme BackButton => new()
    {
        Normal = new Attribute(White, Orange),
        Focus = new Attribute(White, DarkOrange),
        HotNormal = new Attribute(White, Orange),
        HotFocus = new Attribute(White, DarkOrange),
    };

    // Blue button (hover = DarkBlue)
    public static Scheme BlueButton => new()
    {
        Normal = new Attribute(White, Blue),
        Focus = new Attribute(White, DarkBlue),
        HotNormal = new Attribute(White, Blue),
        HotFocus = new Attribute(White, DarkBlue),
    };

    // Green button (hover = DarkGreen)
    public static Scheme GreenButton => new()
    {
        Normal = new Attribute(White, Green),
        Focus = new Attribute(White, DarkGreen),
        HotNormal = new Attribute(White, Green),
        HotFocus = new Attribute(White, DarkGreen),
    };

    // Purple button (hover = DarkPurple)
    public static Scheme PurpleButton => new()
    {
        Normal = new Attribute(White, Purple),
        Focus = new Attribute(White, DarkPurple),
        HotNormal = new Attribute(White, Purple),
        HotFocus = new Attribute(White, DarkPurple),
    };

    // Red button (hover = DarkRed)
    public static Scheme RedButton => new()
    {
        Normal = new Attribute(White, Red),
        Focus = new Attribute(White, DarkRed),
        HotNormal = new Attribute(White, Red),
        HotFocus = new Attribute(White, DarkRed),
    };

    // Gray button
    public static Scheme GrayButton => new()
    {
        Normal = new Attribute(White, SlateGray),
        Focus = new Attribute(White, new Color(60, 70, 85)),
        HotNormal = new Attribute(White, SlateGray),
        HotFocus = new Attribute(White, new Color(60, 70, 85)),
    };

    // Modal window (grey background, light border)
    public static Scheme Window => new()
    {
        Normal = new Attribute(White, ModalGrey),
        Focus = new Attribute(White, ModalGrey),
        HotNormal = new Attribute(White, ModalGrey),
        HotFocus = new Attribute(White, ModalGrey),
    };

    // Title text - transparent background to show shader through
    public static Scheme Title => new()
    {
        Normal = new Attribute(White, Transparent),
    };

    // Hint text
    public static Scheme Hint => new()
    {
        Normal = new Attribute(MediumGrey, Black),
    };

    // Error text
    public static Scheme ErrorText => new()
    {
        Normal = new Attribute(Red, Black),
    };

    // Transparent window for shader background
    public static Scheme TransparentWindow => new()
    {
        Normal = new Attribute(White, ModalGrey),
        Focus = new Attribute(White, ModalGrey),
        HotNormal = new Attribute(White, ModalGrey),
        HotFocus = new Attribute(White, ModalGrey),
    };

    // Inner panel (slightly darker than modal background)
    public static Scheme InnerPanel => new()
    {
        Normal = new Attribute(White, InnerPanelGrey),
        Focus = new Attribute(White, InnerPanelGrey),
        HotNormal = new Attribute(White, InnerPanelGrey),
        HotFocus = new Attribute(White, InnerPanelGrey),
    };

    // ListView - distinct highlight for selected row
    public static Scheme ListView => new()
    {
        Normal = new Attribute(White, ModalGrey),
        Focus = new Attribute(White, Blue),
        HotNormal = new Attribute(White, ModalGrey),
        HotFocus = new Attribute(White, Blue),
    };
}
