using System;

namespace Motely.TUI;

public class BalatroShaderBackground : View
{
    private double _time;
    private double _spinTime;
    private bool _isRunning;

    private Color[,]? _frameBuffer;
    private int _bufferWidth;
    private int _bufferHeight;

    private static readonly (int R, int G, int B) Color1 = (254, 95, 85); // #FE5F55 Red
    private static readonly (int R, int G, int B) Color2 = (0, 157, 255); // #009dff Blue
    private static readonly (int R, int G, int B) Color3 = (55, 66, 68); // #374244 Black

    private const double Contrast = 1.8;
    
    //private const double SpinAmount = 0.6;
    private const double SpinAmount = 0.6;

    //private const double SpinEase = 0.5;
    private const double SpinEase = 0.7;


    //private const double PixelSize = 750.0;
    private const double PixelSize = 128.0;

    //private const double ParallaxX = 0.12;
    private const double ParallaxX = -0.01;

    //private const int LoopCount = 5;
    private const int LoopCount = 3;

    public BalatroShaderBackground()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = false;

        DrawingContent += (s, e) => DrawToScreen();
    }

    public Color GetColorAt(int screenX, int screenY)
    {
        if (_frameBuffer == null || screenY < 0 || screenY >= _bufferHeight / 2)
            return new Color(Color3.R, Color3.G, Color3.B);

        // Convert screen Y to buffer Y (each char shows 2 vertical pixels via half-block)
        int bufferY = screenY * 2;
        if (screenX < 0 || screenX >= _bufferWidth)
            return new Color(Color3.R, Color3.G, Color3.B);

        return _frameBuffer[screenX, bufferY];
    }

    public void Start()
    {
        if (_isRunning)
            return;
        _isRunning = true;

        // Calculate initial frame immediately
        UpdateFrameBuffer();

        MotelyTUI.App?.AddTimeout(
            TimeSpan.FromMilliseconds(31),
            () =>
            {
                if (!_isRunning)
                    return false;

                _time += 0.04;
                _spinTime += 0.03;
                UpdateFrameBuffer();
                SetNeedsDraw();
                return true;
            }
        );
    }

    public void Stop() => _isRunning = false;

    private void UpdateFrameBuffer()
    {
        try
        {
            int screenCols = MotelyTUI.App?.Driver?.Cols ?? 80;
            int screenRows = MotelyTUI.App?.Driver?.Rows ?? 24;

            // Half-block: 2 vertical pixels per character
            int bufferWidth = screenCols;
            int bufferHeight = screenRows * 2;

            if (
                _frameBuffer == null
                || _bufferWidth != bufferWidth
                || _bufferHeight != bufferHeight
            )
            {
                _frameBuffer = new Color[bufferWidth, bufferHeight];
                _bufferWidth = bufferWidth;
                _bufferHeight = bufferHeight;
            }

            double resolution = Math.Sqrt(bufferWidth * bufferWidth + bufferHeight * bufferHeight);
            CalculateFrame(bufferWidth, bufferHeight, resolution);
        }
        catch { }
    }

    public void DrawToScreen()
    {
        if (_frameBuffer == null || MotelyTUI.App?.Driver == null)
            return;

        var driver = MotelyTUI.App.Driver;
        var upperHalf = new System.Text.Rune('▀');
        int maxRows = Math.Min(driver.Rows - 1, _bufferHeight / 2); // -1 to leave room for status line
        int maxCols = Math.Min(driver.Cols, _bufferWidth);

        // Half-block: each char = 2 vertical pixels (top/bottom)
        for (int screenY = 0; screenY < maxRows; screenY++)
        {
            int bufY = screenY * 2;
            for (int screenX = 0; screenX < maxCols; screenX++)
            {
                // Skip the very last cell (bottom-right corner) to avoid terminal scroll
                if (screenY == maxRows - 1 && screenX == maxCols - 1)
                    continue;

                var topColor = _frameBuffer[screenX, bufY];
                var bottomColor = bufY + 1 < _bufferHeight ? _frameBuffer[screenX, bufY + 1] : topColor;

                driver.SetAttribute(new Attribute(topColor, bottomColor));
                driver.Move(screenX, screenY);
                driver.AddRune(upperHalf);
            }
        }
    }

    private void CalculateFrame(int width, int height, double resolution)
    {
        double time = _time;
        double spinTime = _spinTime;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                _frameBuffer![x, y] = CalculatePixel(
                    x,
                    y,
                    width,
                    height,
                    resolution,
                    time,
                    spinTime
                );
            }
        }
    }

    private static Color CalculatePixel(
        int x,
        int y,
        int width,
        int height,
        double resolution,
        double time,
        double spinTime
    )
    {
        double pixSize = resolution / PixelSize;

        // Using double-wide blocks for square pixels - no X stretch needed!
        double uvX = ((Math.Floor(x / pixSize) * pixSize - 0.5 * width) / resolution - ParallaxX);
        double uvY = (Math.Floor(y / pixSize) * pixSize - 0.5 * height) / resolution;
        double uvLen = Math.Sqrt(uvX * uvX + uvY * uvY);

        double speed = spinTime * SpinEase * 0.2 + 302.2;
        double newAngle =
            Math.Atan2(uvY, uvX)
            + speed
            - SpinEase * 20.0 * (SpinAmount * uvLen + (1.0 - SpinAmount));

        // Simplified from original: (uv_len * cos(angle) + mid) - mid = uv_len * cos(angle)
        uvX = uvLen * Math.Cos(newAngle);
        uvY = uvLen * Math.Sin(newAngle);

        uvX *= 30.0;
        uvY *= 30.0;

        double animSpeed = time * 2.0;
        double uv2X = uvX + uvY;
        double uv2Y = uvX + uvY;

        for (int i = 0; i < LoopCount; i++)
        {
            double maxUv = Math.Max(uvX, uvY);
            uv2X += Math.Sin(maxUv) + uvX;
            uv2Y += Math.Sin(maxUv) + uvY;
            uvX += 0.5 * Math.Cos(5.1123314 + 0.353 * uv2Y + animSpeed * 0.131121);
            uvY += 0.5 * Math.Sin(uv2X - 0.113 * animSpeed);
            double cosVal = Math.Cos(uvX + uvY);
            double sinVal = Math.Sin(uvX * 0.711 - uvY);
            uvX -= cosVal - sinVal;
            uvY -= cosVal - sinVal;
        }

        double contrastMod = 0.25 * Contrast + 0.5 * SpinAmount + 1.2;
        double paintRes = Math.Sqrt(uvX * uvX + uvY * uvY) * 0.035 * contrastMod;
        paintRes = Math.Clamp(paintRes, 0.0, 2.0);

        double c1p = Math.Max(0.0, 1.0 - contrastMod * Math.Abs(1.0 - paintRes));
        double c2p = Math.Max(0.0, 1.0 - contrastMod * Math.Abs(paintRes));
        double c3p = 1.0 - Math.Min(1.0, c1p + c2p);

        double cf = 0.3 / Contrast;
        double ncf = 1.0 - cf;

        int r = (int)(cf * Color1.R + ncf * (Color1.R * c1p + Color2.R * c2p + Color3.R * c3p));
        int g = (int)(cf * Color1.G + ncf * (Color1.G * c1p + Color2.G * c2p + Color3.G * c3p));
        int b = (int)(cf * Color1.B + ncf * (Color1.B * c1p + Color2.B * c2p + Color3.B * c3p));

        return new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }
}
