using PKForge.Chrome;
using PKForge.Domain;
using SkiaSharp;

namespace ChromePreview;

/// <summary>Renders the storage box (header, well, one full box, the cursor) at 1920×1080.</summary>
public static class StoragePreview
{
    // (species, form, shiny) for the 30 slots; species 0 is an empty slot.
    private static readonly (int Species, int Form, bool Shiny)[] Box =
    [
        (94, 0, false), (445, 0, true), (658, 0, false), (25, 0, true), (143, 0, false), (131, 0, false),
        (197, 0, true), (282, 0, false), (448, 0, false), (149, 0, true), (384, 0, false), (887, 0, false),
        (700, 0, false), (778, 0, true), (609, 0, false), (248, 0, false), (373, 0, true), (130, 0, true),
        (1000, 0, false), (937, 0, false), (906, 0, true), (908, 0, false), (981, 0, false), (0, 0, false),
        (6, 0, true), (376, 0, false), (635, 0, false), (0, 0, false), (812, 0, false), (1025, 0, true),
    ];

    public static void Render(string root, string output)
    {
        const int width = 1920, height = 1080;
        var pkhex = Path.Combine(root, "external/PKHeX/PKHeX.Drawing.PokeSprite/Resources/img");
        using var sheet = SKBitmap.Decode(Path.Combine(root, "src/PKForge.App/Resources/Showdown/icons.png"));
        using var typeface = SKTypeface.FromFile(Path.Combine(root, "src/PKForge.App/Resources/Fonts/NDS12.ttf"));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var c = surface.Canvas;
        PksmPaint.LogoGrid(c, new SKRect(0, 0, width, height), 24, 3);

        const float unit = 1f;
        using var title = new SKFont(typeface, 34f * unit);
        StoragePaint.BoxHeader(c, new SKRect(48, 154, 1120, 226), "Box 2", title, canPrev: true, canNext: true, unit);
        var well = new SKRect(48, 254, 1120, 956);
        StoragePaint.WellPanel(c, well, unit);

        var area = new SKRect(well.Left + StoragePaint.WellInsetX, well.Top + StoragePaint.WellInsetY,
            well.Right - StoragePaint.WellInsetX, well.Bottom - StoragePaint.WellInsetY);
        float cw = area.Width / 6, ch = area.Height / 5;
        var selected = 6;
        using var star = new SKFont(typeface, 28f * unit);
        var nearest = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        for (var i = 0; i < Box.Length; i++)
        {
            var cell = new SKRect(area.Left + i % 6 * cw, area.Top + i / 6 * ch, area.Left + (i % 6 + 1) * cw, area.Top + (i / 6 + 1) * ch);
            var lift = 0f;
            if (i == selected)
            {
                StoragePaint.CursorPool(c, cell, unit);
                lift = StoragePaint.CursorLift * unit;
            }
            var (species, form, shiny) = Box[i];
            if (species == 0) continue;
            var look = new SpriteLook(species, form, shiny);
            if (ShowdownCatalog.IconCell(look) is not { } found) continue;
            var src = new SKRectI(found.X, found.Y, found.X + found.Width, found.Y + found.Height);
            using var sheetImage = SKImage.FromBitmap(sheet);
            var file = Path.Combine(pkhex, "Big Shiny Sprites", $"b_{species}s.png");
            if (!File.Exists(file)) file = Path.Combine(pkhex, "Artwork Shiny Sprites", $"a_{species}s.png");
            if (!shiny || !File.Exists(file))
            {
                // No shiny art anywhere (most of Gen 9): the normal icon, still starred.
                c.DrawImage(sheetImage, src, StoragePaint.IconRect(cell, unit, lift), nearest, null);
                if (shiny) StoragePaint.ShinyStar(c, cell, star, unit);
                continue;
            }
            using var sprite = SKBitmap.Decode(file);
            var trim = StoragePaint.OpaqueBounds(sprite, new SKRectI(0, 0, sprite.Width, sprite.Height), 16);
            var foot = StoragePaint.OpaqueBounds(sheet, src);
            var footprint = new SKSize(foot.Width * StoragePaint.IconScale * unit, foot.Height * StoragePaint.IconScale * unit);
            using var spriteImage = SKImage.FromBitmap(sprite);
            c.DrawImage(spriteImage, trim, StoragePaint.FootprintRect(cell, trim.Size, footprint, lift, unit), nearest, null);
            StoragePaint.ShinyStar(c, cell, star, unit);
        }
        var cursor = new SKRect(area.Left + selected % 6 * cw, area.Top + selected / 6 * ch, area.Left + (selected % 6 + 1) * cw, area.Top + (selected / 6 + 1) * ch);
        StoragePaint.Pointer(c, cursor, unit);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(output);
        png.SaveTo(stream);
    }
}
