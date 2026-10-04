using PKForge.Chrome;
using SkiaSharp;

namespace ChromePreview;

/// <summary>A few frames of the About screen's easter egg, as the top screen draws them.</summary>
public static class BadApplePreview
{
    public static void Render(string root, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        using var typeface = SKTypeface.FromFile(Path.Combine(root, "src/PKForge.App/Resources/Fonts/NDS12.ttf"));
        using var font = new SKFont(typeface, 20);
        var painter = new BadApplePainter(font, "BULBASAUR·IVYSAUR·VENUSAUR·CHARMANDER·CHARMELEON·CHARIZARD·SQUIRTLE·WARTORTLE·BLASTOISE·");
        using var frames = new BadAppleFrames(File.OpenRead(Path.Combine(root, "src/PKForge.App/Resources/BadApple/frames.bin")));
        foreach (var index in new[] { 400, 1300, 2600, 4200 })
        {
            frames.SeekForward(index);
            using var surface = SKSurface.Create(new SKImageInfo(1920, 1080));
            surface.Canvas.Clear(ColorTheme.Current.Void);
            painter.Paint(surface.Canvas, new SKRect(0, 0, 1920, 1080), frames, font, ColorTheme.Current);
            using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 90);
            using var stream = File.Create(Path.Combine(outputDir, $"badapple_{index}.png"));
            png.SaveTo(stream);
        }
    }
}
