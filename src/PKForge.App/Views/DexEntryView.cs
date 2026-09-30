using PKForge.App.Services;
using PKForge.Chrome;
using PKForge.Domain;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>
/// A species' Pokédex page on the second screen, laid out like the Pokémon summary: on the
/// left the name banner, Showdown's front sprite and the species card (number, types); on
/// the right a panel with where it comes from and its base stats, abilities and gender
/// ratio. Drawn in design pixels of the 1240×1080 screen, scaled to fit.
/// </summary>
public sealed class DexEntryView : SKCanvasView
{
    private const float DesignWidth = 1240, DesignHeight = 1080, LeftWidth = 648;
    private static readonly SKColor PanelFill = new(0x04, 0x24, 0x4E);
    private static readonly SKColor LabelColumn = new(0x06, 0x19, 0x39);
    private static readonly SKColor LabelInk = new(0x28, 0x46, 0x82);
    private static readonly SKColor ValueInk = new(0x96, 0xA8, 0xD2);
    private static readonly SKColor SubInk = new(0x60, 0x7E, 0xBA);
    private static readonly SKColor Cyan = new(0x16, 0xB6, 0xDC);

    private readonly ISpriteService _sprites;
    private int _species;
    private string _name = "";
    private IReadOnlyList<int> _types = [];
    private BaseStats? _stats;
    private SpeciesCard? _card;
    private IReadOnlyList<string> _abilityNames = [];

    public DexEntryView(ISpriteService sprites)
    {
        _sprites = sprites;
        InputTransparent = true;
        PaintSurface += OnPaint;
    }

    /// <summary>Shows one species, read from the open save when there is one.</summary>
    public void Show(int species)
    {
        _species = species;
        var services = IPlatformApplication.Current?.Services;
        var data = services?.GetService<IGameDataService>();
        var session = services?.GetService<ISaveSessionService>()?.CurrentSession;
        _name = data is not null && (uint)species < (uint)data.SpeciesNames.Count ? data.SpeciesNames[species] : $"#{species}";
        _abilityNames = data?.AbilityNames ?? [];
        _types = session?.GetSpeciesTypes(species) ?? [];
        _stats = session?.GetBaseStats(species);
        _card = session is null ? null : InfoPickers.Info?.GetSpeciesCard(session, species, 0);
        InvalidateSurface();
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var c = args.Surface.Canvas;
        c.Clear(SKColors.Transparent);
        if (_species <= 0) return;
        var unit = Math.Min(args.Info.Width / DesignWidth, args.Info.Height / DesignHeight);
        c.Scale(unit);
        var width = args.Info.Width / unit;
        var height = args.Info.Height / unit;

        PaintLeft(c, height);
        PaintPanel(c, width, height);
    }

    private void PaintLeft(SKCanvas c, float height)
    {
        // The name banner, run off the left edge like the summary's.
        using (var frame = new SKPaint { Color = new SKColor(0x1D, 0x22, 0x44), IsAntialias = true })
            c.DrawRoundRect(new SKRect(-40, 10, 600, 104), 26, 26, frame);
        using (var banner = new SKPath())
        {
            banner.MoveTo(-40, 34);
            banner.LineTo(530, 34);
            banner.LineTo(570, 118);
            banner.LineTo(-40, 118);
            banner.Close();
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 34), new SKPoint(0, 118),
                    [StoragePaint.BannerTop, StoragePaint.BannerBottom], SKShaderTileMode.Clamp),
            };
            c.DrawPath(banner, fill);
            using var rim = new SKPaint { Color = Pksm.Ink, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
            c.DrawPath(banner, rim);
        }
        SummaryInk.Draw(c, SummaryInk.Fit(_name, 44, 520), 270, SummaryInk.Center(77, 44), 44, Pksm.Ink, align: SKTextAlign.Center);

        // The species card sits on the bottom edge; the Pokémon stands above it.
        var cardTop = Math.Max(640, height - 390);
        var middle = (118 + cardTop) / 2 + 6;
        void Redraw() => MainThread.BeginInvokeOnMainThread(InvalidateSurface);
        var look = new SpriteLook(_species, 0, false);
        if (_sprites.TryGetShowdownFront(look, Redraw, out var sprite))
        {
            sprite ??= _sprites.GetSprite(look);
            if (sprite is null) _sprites.Warm(look, Redraw);
            else
            {
                var k = Math.Clamp(MathF.Floor(Math.Min(520f / sprite.Width, 500f / sprite.Height)), 1, 7);
                var w = sprite.Width * k;
                var h = sprite.Height * k;
                using var image = SKImage.FromBitmap(sprite);
                c.DrawImage(image, new SKRect(300 - w / 2, middle - h / 2, 300 + w / 2, middle + h / 2),
                    new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
        }

        var card = new SKRect(24, cardTop, 590, cardTop + 430);
        using (var fill = new SKPaint { Color = new SKColor(0x0C, 0x14, 0x2C), IsAntialias = true }) c.DrawRoundRect(card, 28, 28, fill);
        using (var rim = new SKPaint { Color = new SKColor(0x28, 0x46, 0x82), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
            c.DrawRoundRect(SKRect.Inflate(card, -1.5f, -1.5f), 28, 28, rim);
        using (var tab = new SKPath())
        {
            tab.MoveTo(48, cardTop);
            tab.LineTo(384, cardTop);
            tab.LineTo(420, cardTop + 78);
            tab.LineTo(48, cardTop + 78);
            tab.ArcTo(new SKRect(24, cardTop + 30, 72, cardTop + 78), 90, 90, false);
            tab.LineTo(24, cardTop + 24);
            tab.ArcTo(new SKRect(24, cardTop, 72, cardTop + 48), 180, 90, false);
            tab.Close();
            using var fill = new SKPaint { Color = new SKColor(0x10, 0x89, 0xB6), IsAntialias = true };
            c.DrawPath(tab, fill);
            using var rim = new SKPaint { Color = new SKColor(0x5A, 0xD2, 0xF0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
            c.DrawPath(tab, rim);
        }
        SummaryInk.Draw(c, $"No. {_species:000}", 52, SummaryInk.Center(cardTop + 39, 42), 42, new SKColor(0xC8, 0xF0, 0xFF));
        var types = _types.Where(TypeFacts.IsValid).ToList();
        var x = 307 - (types.Count * 184 + Math.Max(0, types.Count - 1) * 20) / 2f;
        foreach (var type in types)
        {
            TypePlates.Paint(c, new SKRect(x, cardTop + 106, x + 184, cardTop + 166), type);
            x += 204;
        }
        if (DexRegions.Of(_species) is { } region)
        {
            using var band = new SKPaint { Color = new SKColor(0x19, 0x24, 0x47) };
            c.DrawRect(new SKRect(27, cardTop + 194, 587, cardTop + 286), band);
            SummaryInk.Draw(c, "Region", 60, SummaryInk.Center(cardTop + 240, 40), 40, LabelInk);
            SummaryInk.Draw(c, $"{region.Name} · Gen {region.Roman}", 560, SummaryInk.Center(cardTop + 240, 40), 40, ValueInk, align: SKTextAlign.Right);
        }
    }

    private void PaintPanel(SKCanvas c, float width, float height)
    {
        var panel = new SKRect(LeftWidth, 40, width + 40, height + 40);
        using (var fill = new SKPaint { Color = PanelFill, IsAntialias = true }) c.DrawRoundRect(panel, 40, 40, fill);
        using (var rim = new SKPaint { Color = Cyan, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 })
            c.DrawRoundRect(SKRect.Inflate(panel, -2, -2), 38, 38, rim);
        var left = LeftWidth + 20;
        var right = width - 20;
        var y = 70f;

        void Section(string title)
        {
            EditorPaint.PaintSection(c, new SKRect(left - 18, y, right + 18, y + 96), title, PixelFont.Face, 34f / 30f);
            y += 96;
        }
        void Row(string label, string value, SKColor? tone = null)
        {
            using (var column = new SKPaint { Color = LabelColumn }) c.DrawRect(new SKRect(LeftWidth + 4, y, LeftWidth + 252, y + 70), column);
            SummaryInk.Draw(c, label, LeftWidth + 42, SummaryInk.Center(y + 36, 36), 36, LabelInk);
            SummaryInk.Draw(c, SummaryInk.Fit(value, 36, right - (LeftWidth + 282) - 8), LeftWidth + 282, SummaryInk.Center(y + 36, 36), 36, tone ?? ValueInk);
            y += 76;
        }

        if (_stats is { } stats)
        {
            Section("Base stats");
            string[] names = ["HP", "Attack", "Defense", "Sp. Atk", "Sp. Def", "Speed"];
            int[] values = [stats.Hp, stats.Atk, stats.Def, stats.SpA, stats.SpD, stats.Spe];
            for (var i = 0; i < 6; i++) Row(names[i], values[i].ToString());
            Row("Total", values.Sum().ToString(), Pksm.Ink);
        }
        if (_card is { } card && y < height - 200)
        {
            Section("Abilities");
            foreach (var ability in card.Abilities)
            {
                if (y > height - 80) break;
                var name = (uint)ability.Id < (uint)_abilityNames.Count ? _abilityNames[ability.Id] : $"#{ability.Id}";
                Row(ability.Slot == "Hidden" ? "Hidden" : $"Slot {ability.Slot}", name, ability.Slot == "Hidden" ? Pksm.ShinyGold : null);
            }
            if (card.Gender is { } gender && y <= height - 80)
                SummaryInk.Draw(c, SummaryInk.Fit(gender.Label, 30, right - left - 20), (left + right) / 2, SummaryInk.Center(y + 30, 30), 30, SubInk, align: SKTextAlign.Center);
        }
    }
}
