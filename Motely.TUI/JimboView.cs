using System.Reflection;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;
using SLImage = SixLabors.ImageSharp.Image;

namespace Motely.TUI;

/// <summary>
/// Renders the Jimbo sprite as colored pixel blocks in the terminal.
/// Uses half-block characters for 2x vertical resolution.
/// Loads from embedded Jimbo.png with proper alpha channel support.
/// </summary>
public class JimboView : View
{
    private readonly Color[,] _pixels;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly double _scale;

    /// <summary>Width in character cells (for layout).</summary>
    public int DisplayWidth => (int)(_pixelWidth * _scale);

    public JimboView(double scale = 0.65)
    {
        _scale = scale;
        (_pixels, _pixelWidth, _pixelHeight) = LoadFromPng();

        // Scale down dimensions
        var scaledWidth = (int)(_pixelWidth * _scale);
        var scaledHeight = (int)(_pixelHeight * _scale);

        // Each character cell shows 2 vertical pixels using half-block
        Width = scaledWidth;
        Height = (scaledHeight + 1) / 2;
        CanFocus = false;
        DrawingContent += (s, e) => DrawContent();
    }

    private static (Color[,] pixels, int width, int height) LoadFromPng()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Motely.TUI.Jimbo.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return CreatePlaceholder(8, 8);

            using var image = SLImage.Load<Rgba32>(stream);
            int width = image.Width;
            int height = image.Height;
            var pixels = new Color[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = image[x, y];
                    pixels[x, y] = new Color(pixel.R, pixel.G, pixel.B, pixel.A);
                }
            }

            return (pixels, width, height);
        }
        catch
        {
            return CreatePlaceholder(8, 8);
        }
    }

    private static (Color[,] pixels, int width, int height) CreatePlaceholder(int w, int h)
    {
        var placeholder = new Color[w, h];
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            placeholder[x, y] = new Color(255, 0, 255);
        return (placeholder, w, h);
    }

    private void DrawContent()
    {
        var viewport = Viewport;
        var transparent = new Color(0, 0, 0, 0);
        var shader = MotelyTUI.ShaderBackground;
        var screenRect = ViewportToScreen(viewport);

        var scaledWidth = (int)(_pixelWidth * _scale);
        var scaledHeight = (int)(_pixelHeight * _scale);

        for (int charY = 0; charY < viewport.Height && charY * 2 < scaledHeight; charY++)
        {
            for (int charX = 0; charX < viewport.Width && charX < scaledWidth; charX++)
            {
                // Scale coordinates back to original pixel coordinates (nearest-neighbor sampling)
                int srcX = (int)(charX / _scale);
                int srcTopY = (int)((charY * 2) / _scale);
                int srcBottomY = (int)((charY * 2 + 1) / _scale);

                // Clamp to valid pixel bounds
                srcX = Math.Clamp(srcX, 0, _pixelWidth - 1);
                srcTopY = Math.Clamp(srcTopY, 0, _pixelHeight - 1);
                srcBottomY = Math.Clamp(srcBottomY, 0, _pixelHeight - 1);

                var topColor = _pixels[srcX, srcTopY];
                var bottomColor =
                    srcBottomY < _pixelHeight ? _pixels[srcX, srcBottomY] : transparent;

                bool topTransparent = topColor.A == 0;
                bool bottomTransparent = bottomColor.A == 0;

                // Get shader background color at this screen position
                var bgColor =
                    shader?.GetColorAt(screenRect.X + charX, screenRect.Y + charY) ?? Color.Black;

                if (topTransparent && bottomTransparent)
                {
                    // Both transparent - draw background shader color
                    SetAttribute(new Attribute(bgColor, bgColor));
                    AddRune(charX, charY, (Rune)'█');
                    continue;
                }

                if (topTransparent)
                {
                    // Only bottom pixel visible - lower half block, shader on top
                    SetAttribute(new Attribute(bottomColor, bgColor));
                    AddRune(charX, charY, (Rune)'▄');
                }
                else if (bottomTransparent)
                {
                    // Only top pixel visible - upper half block, shader on bottom
                    SetAttribute(new Attribute(topColor, bgColor));
                    AddRune(charX, charY, (Rune)'▀');
                }
                else
                {
                    // Both pixels visible - upper half block with fg=top, bg=bottom
                    SetAttribute(new Attribute(topColor, bottomColor));
                    AddRune(charX, charY, (Rune)'▀');
                }
            }
        }
    }
}
