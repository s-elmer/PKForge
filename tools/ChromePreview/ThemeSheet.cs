using PKForge.Chrome;
using SkiaSharp;

namespace ChromePreview;

/// <summary>
/// Renders the storage box and the editor panel in every color theme, side by side with the
/// artist's mockup for that theme, to check tools/Themes/build.py's palettes by eye.
/// </summary>
public static class ThemeSheet
{
    public static void Render(string root, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var scratch = Path.Combine(outputDir, "parts");
        Directory.CreateDirectory(scratch);
        foreach (var theme in ColorThemes.All)
        {
            ColorTheme.Apply(theme);
            var storage = Path.Combine(scratch, $"{theme.Id}-storage.png");
            var editor = Path.Combine(scratch, $"{theme.Id}-editor.png");
            StoragePreview.Render(root, storage);
            EditorPreview.Render(root, editor);

            using var box = SKBitmap.Decode(storage);
            using var panel = SKBitmap.Decode(editor);
            using var mockup = SKBitmap.Decode(Path.Combine(root, "tools/Themes/mockups", $"{theme.Id}.png"));
            const int height = 1080;
            var boxWidth = box.Width * height / box.Height;
            var panelWidth = panel.Width * height / panel.Height;
            var mockupWidth = mockup.Width * height / mockup.Height;
            using var surface = SKSurface.Create(new SKImageInfo(boxWidth + panelWidth + mockupWidth, height));
            var c = surface.Canvas;
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            using (var image = SKImage.FromBitmap(box)) c.DrawImage(image, SKRect.Create(0, 0, boxWidth, height), sampling);
            using (var image = SKImage.FromBitmap(panel)) c.DrawImage(image, SKRect.Create(boxWidth, 0, panelWidth, height), sampling);
            using (var image = SKImage.FromBitmap(mockup)) c.DrawImage(image, SKRect.Create(boxWidth + panelWidth, 0, mockupWidth, height), sampling);
            using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 90);
            using var stream = File.Create(Path.Combine(outputDir, $"{theme.Id}.png"));
            png.SaveTo(stream);
        }
        ColorTheme.Apply(ColorThemes.Default);
        RenderPicker(outputDir);
    }

    /// <summary>Every theme's picker tile on one sheet, as the color scheme picker draws them.</summary>
    private static void RenderPicker(string outputDir)
    {
        const int columns = 4, tileW = 480, tileH = 300, gap = 24;
        var rows = (ColorThemes.All.Count + columns - 1) / columns;
        using var surface = SKSurface.Create(new SKImageInfo(columns * (tileW + gap) + gap, rows * (tileH + gap) + gap));
        var c = surface.Canvas;
        c.Clear(new SKColor(0x10, 0x18, 0x30));
        using var face = SKTypeface.FromFamilyName("Helvetica");
        for (var i = 0; i < ColorThemes.All.Count; i++)
            ThemePreviewPaint.Paint(c, SKRect.Create(gap + i % columns * (tileW + gap), gap + i / columns * (tileH + gap), tileW, tileH), ColorThemes.All[i], face);
        using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(Path.Combine(outputDir, "picker.png"));
        png.SaveTo(stream);
    }
}
