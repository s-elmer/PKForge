using PKForge.Chrome;
using SkiaSharp;

namespace ChromePreview;

/// <summary>
/// Renders the editor panel at the mockup's 740 px width: the header, the section chips and
/// the watermark come from EditorPaint; the rows (MAUI views in the app) are drawn here with
/// the same bands, colors and sizes as a stand-in.
/// </summary>
public static class EditorPreview
{
    public static void Render(string root, string output)
    {
        const int width = 780, height = 1300;
        const float unit = 1f;
        using var typeface = SKTypeface.FromFile(Path.Combine(root, "src/PKForge.App/Resources/Fonts/NDS12.ttf"));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var c = surface.Canvas;
        PksmPaint.LogoGrid(c, new SKRect(0, 0, width, height), 24, 3);

        var panel = new SKRect(20, 20, 20 + EditorPaint.DesignWidth, height - 20);
        StoragePaint.WellPanel(c, panel, unit);
        var pkhex = Path.Combine(root, "external/PKHeX/PKHeX.Drawing.PokeSprite/Resources/img/Big Shiny Sprites/b_197s.png");
        using (var sprite = SKBitmap.Decode(pkhex))
        {
            var trim = StoragePaint.OpaqueBounds(sprite, new SKRectI(0, 0, sprite.Width, sprite.Height), 16);
            using var cropped = new SKBitmap();
            sprite.ExtractSubset(cropped, trim);
            using var image = SKImage.FromBitmap(cropped);
            EditorPaint.PaintWatermark(c, panel, image, unit, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
        EditorPaint.PaintHeader(c, new SKRect(panel.Left, panel.Top, panel.Right, panel.Top + EditorPaint.DesignHeaderHeight),
            new EditorPaint.Header("Midnight", 41, true, 1, "Umbreon", "EXP 68,921 at Lv 41  ·  5,167 to Lv 42"), typeface, unit);

        using var font = new SKFont(typeface, 32f * unit);
        var y = panel.Top + EditorPaint.DesignHeaderHeight + 20f * unit;
        var dark = true;
        void Row(string label, string value, bool chevron = true, SKColor? valueColor = null)
        {
            var band = new SKRect(panel.Left + 3, y, panel.Right - 3, y + 64f * unit);
            using (var fill = new SKPaint { Color = EditorPaint.RowBand(dark) }) c.DrawRect(band, fill);
            Draw(label, panel.Left + 34f * unit, band.MidY, EditorPaint.Label);
            Draw(value, panel.Left + 248f * unit, band.MidY, valueColor ?? EditorPaint.Value);
            if (chevron) Draw("›", panel.Right - 44f * unit, band.MidY, EditorPaint.Value);
            y += 64f * unit;
            dark = !dark;
        }
        void Section(string title)
        {
            EditorPaint.PaintSection(c, new SKRect(panel.Left, y, panel.Right, y + 64f * unit), title, typeface, unit);
            y += 64f * unit;
            dark = true;
        }
        void Draw(string text, float x, float cy, SKColor color)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var m = font.Metrics;
            c.DrawText(text, x, cy - (m.Ascent + m.Descent) / 2, SKTextAlign.Left, font, paint);
        }

        Row("Species", "Umbreon");
        Row("Nickname", "Midnight", chevron: false);
        Row("Gender", "♀            Shiny  ★", chevron: false);
        Row("Level", "41");
        Section("PKMN Info");
        Row("Nature", "Calm");
        Row("Ability", "Synchronize");
        Row("Held Item", "Leftovers");
        Row("Friendship", "255");
        Section("PKMN Moves");
        Row("Dark", "Foul Play");
        Section("Legality");
        Row("Status", "Legal", valueColor: Pksm.Legal);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(output);
        png.SaveTo(stream);
    }
}
