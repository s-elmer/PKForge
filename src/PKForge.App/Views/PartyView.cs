using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.Chrome;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Views;

/// <summary>
/// The party view (box -1), laid out like the games' own party screen: two staggered
/// columns of rounded cards joined by a circuit trace. Each card carries a Poké Ball
/// plate behind the Pokémon's box sprite, the nickname and gender, an HP tag and bar,
/// the level and the HP numbers. Empty slots are striped recesses; fainted cards turn
/// rust. Live state from the session.
/// </summary>
public static class PartyView
{
    public const int Count = 6;
    private const int Columns = 2;
    private const int Rows = 3;

    private static SKFont? _nameFontFallback;
    private static SKFont? _nameFontPixel;
    private static SKFont? _smallFont;
    private static SKFont? _labelFont;

    private static readonly SKColor Bg = Pksm.LogoVoid;
    private static readonly SKColor BgLine = Pksm.LogoDeck;
    private static readonly SKColor Trace = Pksm.LogoBlue.WithAlpha(0xC8);
    private static readonly SKColor BodyTop = new(0x2F, 0x5B, 0xB0);
    private static readonly SKColor BodyBottom = new(0x1F, 0x3C, 0x7C);
    private static readonly SKColor SelectedTop = new(0x3C, 0x71, 0xCC);
    private static readonly SKColor SelectedBottom = new(0x2A, 0x4E, 0x98);
    private static readonly SKColor Edge = Pksm.LogoBlue;
    private static readonly SKColor Gloss = SKColors.White.WithAlpha(0x1C);
    private static readonly SKColor EmptyBody = Pksm.LogoDeep;
    private static readonly SKColor EmptyStripe = Pksm.LogoDeck;
    private static readonly SKColor EmptyEdge = Pksm.LogoGrid.WithAlpha(0x90);
    private static readonly SKColor FaintTop = new(0x6A, 0x36, 0x2C);
    private static readonly SKColor FaintBottom = new(0x42, 0x22, 0x1C);
    private static readonly SKColor FaintEdge = new(0xA0, 0x5A, 0x44);
    private static readonly SKColor Track = Pksm.LogoVoid;
    private static readonly SKColor HpTag = Pksm.Legal;
    private static readonly SKColor TextShadow = Pksm.LogoVoid.WithAlpha(0xC0);
    private static readonly SKColor Selected = Pksm.LogoCyan;
    private static readonly SKColor Cursor = Pksm.Ink;
    private const byte BallAlpha = 0x48;

    public static void Paint(SKCanvas canvas, SKImageInfo info, ISpriteService sprites, ISaveEngineSession? session, int selectedSlot, Action invalidate, (int Box, int Slot)? carrySource = null, float pulsePhase = 0f,
        Func<int, bool>? isMarked = null, Func<int, bool?>? rangeMark = null)
    {
        using (var bg = new SKPaint { Color = Bg })
            canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), bg);
        using (var line = new SKPaint { Color = BgLine, StrokeWidth = 1 })
        {
            for (float x = 0; x < info.Width; x += 32) canvas.DrawLine(x, 0, x, info.Height, line);
            for (float y = 0; y < info.Height; y += 32) canvas.DrawLine(0, y, info.Width, y, line);
        }
        DrawTrace(canvas, info);

        var swapping = SwapProgress();
        var slidingCards = new List<(int Slot, EntityDetail? Detail)>();
        foreach (var i in Enumerable.Range(0, Count))
        {
            var rect = SlotRect(info, i);
            EntityDetail? detail = null;
            try { detail = session?.ReadEntity(-1, i); } catch { /* engine validates coordinates */ }
            // The carried Pokémon stays on its own card, lifted and breathing, until the
            // second A drops it; the cursor alone shows where it will go.
            var heldHere = carrySource is { Box: -1, Slot: var src } && src == i && detail is { IsEmpty: false };
            var breath = heldHere ? 1f + 0.028f * (0.5f + 0.5f * MathF.Sin(pulsePhase)) : 1f;
            var pulsed = breath == 1f ? rect : ScaleRect(rect, breath);

            if (swapping is { } sw && (i == sw.A || i == sw.B))
            {
                // Drawn last, over the other cards, while the two slide past each other.
                slidingCards.Add((i, detail));
                continue;
            }

            Slot(canvas, pulsed, detail, sprites, i == selectedSlot && isMarked is null && !heldHere, invalidate, lifted: heldHere);

            // Multi-select: the same wash, green hand and check badge as the box grid.
            if (isMarked is null) continue;
            if (rangeMark?.Invoke(i) is { } mark) PksmPaint.RangeWash(canvas, rect, mark);
            if (i == selectedSlot) PksmPaint.Selection(canvas, rect, Pksm.CursorGreen);
            if (detail is { IsEmpty: false } && isMarked(i)) PksmPaint.MarkBadge(canvas, rect);
        }

        if (swapping is { } swap)
        {
            // Each card glides from the slot it left to the one it now holds.
            var eased = 1f - MathF.Pow(1f - swap.T, 3f);
            foreach (var (slot, detail) in slidingCards)
            {
                var from = SlotRect(info, slot == swap.A ? swap.B : swap.A);
                var to = SlotRect(info, slot);
                var at = new SKRect(Lerp(from.Left, to.Left, eased), Lerp(from.Top, to.Top, eased),
                    Lerp(from.Right, to.Right, eased), Lerp(from.Bottom, to.Bottom, eased));
                Slot(canvas, at, detail, sprites, slot == selectedSlot && swap.T >= 1f, invalidate);
            }
            if (swap.T < 1f) invalidate();
        }
    }

    private const float SwapMilliseconds = 220f;
    private static (int A, int B, long Start)? _swap;

    /// <summary>Starts the card swap animation after two party slots traded Pokémon.</summary>
    public static void BeginSwap(int a, int b) => _swap = (a, b, Environment.TickCount64);

    private static (int A, int B, float T)? SwapProgress()
    {
        if (_swap is not { } swap) return null;
        var t = (Environment.TickCount64 - swap.Start) / SwapMilliseconds;
        if (t >= 1f) { _swap = null; return null; }
        return (swap.A, swap.B, Math.Clamp(t, 0f, 1f));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static SKRect ScaleRect(SKRect r, float scale)
    {
        var w = r.Width * (scale - 1f) / 2f;
        var h = r.Height * (scale - 1f) / 2f;
        return new SKRect(r.Left - w, r.Top - h, r.Right + w, r.Bottom + h);
    }

    /// <summary>Maps a touch point to a slot index (two staggered columns), -1 outside.</summary>
    public static int SlotFromTouch(SKSize canvasSize, SKPoint point)
    {
        for (var i = 0; i < Count; i++)
            if (SlotRect(new SKImageInfo((int)canvasSize.Width, (int)canvasSize.Height), i).Contains(point.X, point.Y))
                return i;
        return -1;
    }

    private static (float Margin, float GapX, float GapY, float Stagger, float Width, float Height) Grid(SKImageInfo info)
    {
        var margin = MathF.Round(info.Width * 0.022f);
        var gapX = MathF.Round(info.Width * 0.026f);
        var stagger = MathF.Round(info.Height * 0.055f);
        var gapY = MathF.Round(info.Height * 0.03f);
        var width = (info.Width - margin * 2 - gapX) / Columns;
        var height = (info.Height - margin * 2 - stagger - gapY * (Rows - 1)) / Rows;
        return (margin, gapX, gapY, stagger, width, height);
    }

    /// <summary>Slots 0, 2, 4 run down the left column; 1, 3, 5 down the right one, a
    /// half-step lower, like the games' party screen.</summary>
    private static SKRect SlotRect(SKImageInfo info, int index)
    {
        var g = Grid(info);
        var col = index % Columns;
        var row = index / Columns;
        var x = g.Margin + col * (g.Width + g.GapX);
        var y = g.Margin + row * (g.Height + g.GapY) + (col == 1 ? g.Stagger : 0);
        return new SKRect(x, y, x + g.Width, y + g.Height);
    }

    /// <summary>The circuit trace behind the cards: a spine in the column gap that jogs
    /// between the two columns, with a stub into every card.</summary>
    private static void DrawTrace(SKCanvas canvas, SKImageInfo info)
    {
        var g = Grid(info);
        var spine = g.Margin + g.Width + g.GapX / 2;
        using var paint = new SKPaint { Color = Trace, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
        using var path = new SKPath();
        for (var row = 0; row < Rows; row++)
        {
            var left = SlotRect(info, row * 2);
            var right = SlotRect(info, row * 2 + 1);
            var yLeft = left.MidY - left.Height * 0.12f;
            var yRight = right.MidY + right.Height * 0.12f;
            path.MoveTo(left.Right - 6, yLeft);
            path.LineTo(spine, yLeft);
            path.LineTo(spine, yRight);
            path.LineTo(right.Left + 6, yRight);
            if (row < Rows - 1)
            {
                var nextLeft = SlotRect(info, row * 2 + 2);
                path.MoveTo(spine, yRight);
                path.LineTo(spine, nextLeft.MidY - nextLeft.Height * 0.12f);
            }
        }
        canvas.DrawPath(path, paint);
    }

    private static void Slot(SKCanvas canvas, SKRect r, EntityDetail? detail, ISpriteService sprites, bool selected, Action invalidate, bool lifted = false)
    {
        var radius = r.Height * 0.14f;
        var empty = detail is null or { IsEmpty: true };
        var fainted = detail is { IsEmpty: false, CurrentHp: 0 };

        if (lifted)
        {
            using var carryGhost = new SKPaint { Color = Selected.WithAlpha(0x70), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            carryGhost.PathEffect = SKPathEffect.CreateDash([7, 6], 0);
            canvas.DrawRoundRect(r, radius, radius, carryGhost);
            r = SKRect.Inflate(r, 2, -4);
        }

        if (empty)
        {
            if (detail is null) return;
            EmptySlot(canvas, r, radius, selected);
            return;
        }

        // Card body: a vertical gradient with a light diagonal band, then its edge.
        var top = fainted ? FaintTop : selected ? SelectedTop : BodyTop;
        var bottom = fainted ? FaintBottom : selected ? SelectedBottom : BodyBottom;
        using (var drop = new SKPaint { Color = Pksm.LogoVoid.WithAlpha(0xC0), IsAntialias = true })
            canvas.DrawRoundRect(new SKRect(r.Left, r.Top + 4, r.Right, r.Bottom + 4), radius, radius, drop);
        using (var body = new SKPaint { IsAntialias = true })
        {
            body.Shader = SKShader.CreateLinearGradient(new SKPoint(0, r.Top), new SKPoint(0, r.Bottom), [top, bottom], SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(r, radius, radius, body);
        }
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddRoundRect(r, radius, radius);
            canvas.ClipPath(clip, antialias: true);
            using var band = new SKPath();
            band.MoveTo(r.Left + r.Width * 0.58f, r.Top);
            band.LineTo(r.Left + r.Width * 0.80f, r.Top);
            band.LineTo(r.Left + r.Width * 0.66f, r.Bottom);
            band.LineTo(r.Left + r.Width * 0.44f, r.Bottom);
            band.Close();
            using var gloss = new SKPaint { Color = Gloss, IsAntialias = true };
            canvas.DrawPath(band, gloss);
            using var light = new SKPaint { Color = SKColors.White.WithAlpha(0x30), StrokeWidth = 2 };
            canvas.DrawLine(r.Left + radius, r.Top + 2, r.Right - radius, r.Top + 2, light);
        }
        canvas.Restore();
        using (var edge = new SKPaint { Color = fainted ? FaintEdge : Edge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            canvas.DrawRoundRect(SKRect.Inflate(r, -1, -1), radius, radius, edge);

        // The box sprite in the card's left third, its ball faint behind it.
        var ballRadius = r.Height * 0.30f;
        var ballCenter = new SKPoint(r.Left + r.Height * 0.32f, r.Top + r.Height * 0.36f);
        // The Pokémon's own ball, faint behind the sprite: it follows ball edits.
        var ballSprite = sprites.GetBall(detail!.Ball);
        if (ballSprite is not null)
            BallPlate(canvas, ballSprite, ballCenter, ballRadius * 1.05f, fainted);
        else
            sprites.WarmBall(detail.Ball, invalidate);
        var bitmap = sprites.GetSprite(detail.Look);
        if (bitmap is not null)
        {
            var target = r.Height * 0.62f;
            var scale = MathF.Max(1f, MathF.Floor(target / MathF.Max(bitmap.Width, bitmap.Height) * 2f) / 2f);
            var w = bitmap.Width * scale;
            var h = bitmap.Height * scale;
            var spriteBottom = ballCenter.Y + ballRadius * 0.9f;
            using var paint = new SKPaint();
            if (fainted) paint.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0x70, 0x50, 0x48), SKBlendMode.SrcIn);
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, new SKRect(ballCenter.X - w / 2, spriteBottom - h, ballCenter.X + w / 2, spriteBottom),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
        }
        else
        {
            sprites.Warm(detail.Look, invalidate);
        }
        // Name row, gender at the right end.
        var inset = r.Height * 0.12f;
        var tx = r.Left + r.Height * 0.68f;
        var textRight = r.Right - inset;
        var nameFont = NameFontFor(detail.Nickname, r.Height * 0.2f);
        var nameBaseline = r.Top + r.Height * 0.3f;
        var name = Fit(detail.Nickname, nameFont, textRight - tx - r.Height * 0.16f);
        Shadowed(canvas, name, tx, nameBaseline, SKTextAlign.Left, nameFont, fainted ? new SKColor(0xF0, 0xC0, 0xA8) : Pksm.Ink);
        if (detail.Gender is 0 or 1)
            DrawGender(canvas, new SKPoint(textRight - r.Height * 0.06f, nameBaseline - r.Height * 0.075f), r.Height * 0.052f, detail.Gender == 0);

        // HP tag and bar.
        var smallFont = SmallFont(r.Height * 0.17f);
        var labelFont = LabelFont(r.Height * 0.11f);
        var maxHp = detail.Stats is { Count: 6 } ? detail.Stats[0] : 0;
        var barTop = r.Top + r.Height * 0.44f;
        var barHeight = r.Height * 0.12f;
        var tagWidth = labelFont.MeasureText("HP") + r.Height * 0.1f;
        var tag = new SKRect(tx, barTop, tx + tagWidth, barTop + barHeight);
        using (var t = new SKPaint { Color = Pksm.LogoVoid, IsAntialias = true })
            canvas.DrawRoundRect(tag, 3, 3, t);
        using (var t = new SKPaint { Color = HpTag, IsAntialias = true })
            canvas.DrawText("HP", tag.MidX, tag.MidY + labelFont.Size * 0.36f, SKTextAlign.Center, labelFont, t);
        var bar = new SKRect(tag.Right - 2, barTop, textRight, barTop + barHeight);
        using (var track = new SKPaint { Color = Track, IsAntialias = true })
            canvas.DrawRoundRect(bar, 3, 3, track);
        using (var rim = new SKPaint { Color = Pksm.LogoGrid, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            canvas.DrawRoundRect(SKRect.Inflate(bar, -1, -1), 3, 3, rim);
        if (maxHp > 0)
        {
            var ratio = Math.Clamp(detail.CurrentHp / (float)maxHp, 0f, 1f);
            if (ratio > 0f)
            {
                var fill = ratio > 0.5f ? new SKColor(0x3F, 0xE0, 0x7F) : ratio > 0.2f ? new SKColor(0xE8, 0xC8, 0x4A) : new SKColor(0xE8, 0x58, 0x58);
                var inner = SKRect.Inflate(bar, -3, -3);
                using var f = new SKPaint { Color = fill, IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(inner.Left, inner.Top, inner.Left + inner.Width * ratio, inner.Bottom), 2, 2, f);
                using var shine = new SKPaint { Color = SKColors.White.WithAlpha(0x50), StrokeWidth = 2 };
                canvas.DrawLine(inner.Left + 2, inner.Top + 2, inner.Left + inner.Width * ratio - 2, inner.Top + 2, shine);
            }
        }

        // Level under the plate, HP numbers under the bar.
        var bottomBaseline = r.Bottom - r.Height * 0.14f;
        var lvX = r.Left + inset;
        Shadowed(canvas, $"Lv{detail.Level}", lvX, bottomBaseline, SKTextAlign.Left, smallFont, fainted ? new SKColor(0xE0, 0xA9, 0x8A) : Pksm.Ink);
        if (detail.IsShiny)
            DrawShinyStar(canvas, new SKPoint(lvX + smallFont.MeasureText($"Lv{detail.Level}") + r.Height * 0.1f, bottomBaseline - smallFont.Size * 0.35f), r.Height * 0.06f);
        if (maxHp > 0)
            Shadowed(canvas, $"{detail.CurrentHp}/{maxHp}", textRight, bottomBaseline, SKTextAlign.Right, smallFont, Pksm.Ink);

        if (selected && !lifted) SelectionFrame(canvas, r, radius);
    }

    /// <summary>An empty party slot: a dark striped recess, no plate, no text.</summary>
    private static void EmptySlot(SKCanvas canvas, SKRect r, float radius, bool selected)
    {
        using (var body = new SKPaint { Color = EmptyBody, IsAntialias = true })
            canvas.DrawRoundRect(r, radius, radius, body);
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddRoundRect(r, radius, radius);
            canvas.ClipPath(clip, antialias: true);
            using var stripe = new SKPaint { Color = EmptyStripe, StrokeWidth = 3 };
            for (var y = r.Top + 6; y < r.Bottom; y += 8) canvas.DrawLine(r.Left, y, r.Right, y, stripe);
        }
        canvas.Restore();
        using (var edge = new SKPaint { Color = EmptyEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            canvas.DrawRoundRect(SKRect.Inflate(r, -1, -1), radius, radius, edge);
        if (selected) SelectionFrame(canvas, r, radius);
    }

    /// <summary>The cursor: a static white frame around the card, no glow.</summary>
    private static void SelectionFrame(SKCanvas canvas, SKRect r, float radius)
    {
        using var frame = new SKPaint { Color = Cursor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
        canvas.DrawRoundRect(SKRect.Inflate(r, 1, 1), radius + 1, radius + 1, frame);
    }

    /// <summary>The Pokémon's own ball behind its sprite, enlarged at a whole-number scale
    /// so its pixels stay crisp, and faint so the sprite stays in front.</summary>
    private static void BallPlate(SKCanvas canvas, SKBitmap ball, SKPoint c, float radius, bool fainted)
    {
        var scale = MathF.Max(1f, MathF.Floor(radius * 2f / ball.Width));
        var w = ball.Width * scale;
        var h = ball.Height * scale;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(BallAlpha) };
        if (fainted) paint.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0x70, 0x50, 0x48, BallAlpha), SKBlendMode.SrcIn);
        using var image = SKImage.FromBitmap(ball);
        canvas.DrawImage(image, new SKRect(c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
    }

    private static void DrawShinyStar(SKCanvas canvas, SKPoint c, float r)
    {
        using var path = new SKPath();
        for (var k = 0; k < 10; k++)
        {
            var a = MathF.PI / 5 * k - MathF.PI / 2;
            var rr = k % 2 == 0 ? r : r * 0.45f;
            var p = new SKPoint(c.X + rr * MathF.Cos(a), c.Y + rr * MathF.Sin(a));
            if (k == 0) path.MoveTo(p); else path.LineTo(p);
        }
        path.Close();
        using var fill = new SKPaint { Color = Pksm.ShinyGold, IsAntialias = true };
        canvas.DrawPath(path, fill);
    }

    /// <summary>Pixel-game text: the glyphs over a 2 px drop shadow.</summary>
    private static void Shadowed(SKCanvas canvas, string text, float x, float y, SKTextAlign align, SKFont font, SKColor color)
    {
        using (var shadow = new SKPaint { Color = TextShadow, IsAntialias = true })
            canvas.DrawText(text, x + 2, y + 2, align, font, shadow);
        using var fg = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, fg);
    }

    /// <summary>Trims a nickname with an ellipsis so it never runs into the gender mark.</summary>
    private static string Fit(string text, SKFont font, float width)
    {
        if (font.MeasureText(text) <= width) return text;
        for (var n = text.Length - 1; n > 0; n--)
        {
            var candidate = text[..n] + "…";
            if (font.MeasureText(candidate) <= width) return candidate;
        }
        return text;
    }

    /// <summary>Constant-label fonts that re-resolve until the async pixel face
    /// lands — a cached SKTypeface.Default pins Roboto (wider) for the process and
    /// the HP numbers outgrow their reserve.</summary>
    private static SKFont SmallFont(float size) =>
        _smallFont is null || _smallFont.Typeface != PixelFont.Face || _smallFont.Size != size
            ? _smallFont = new SKFont(PixelFont.Face, size) { Edging = SKFontEdging.Antialias, Embolden = true }
            : _smallFont;

    private static SKFont LabelFont(float size) =>
        _labelFont is null || _labelFont.Typeface != PixelFont.Face || _labelFont.Size != size
            ? _labelFont = new SKFont(PixelFont.Face, size) { Edging = SKFontEdging.Antialias, Embolden = true }
            : _labelFont;

    /// <summary>Nicknames use the pixel face like every other label when they are plain
    /// Latin text, and the bundled M PLUS Rounded face otherwise (kana, CJK, symbols). The
    /// pixel subset's cmap claims glyphs it cannot draw, so coverage is decided by code
    /// range, not by probing the font.</summary>
    private static SKFont NameFontFor(string text, float size)
    {
        if (text.All(c => c < 0x180))
        {
            var pixel = PixelFont.Face;
            if (_nameFontPixel is null || _nameFontPixel.Typeface != pixel || _nameFontPixel.Size != size)
                _nameFontPixel = new SKFont(pixel, size) { Edging = SKFontEdging.Antialias, Embolden = true };
            return _nameFontPixel;
        }
        // Re-resolve every paint until the async face load lands: caching on the first
        // call pins SKTypeface.Default (no kana) for the whole process.
        var face = PixelFont.FallbackFace;
        if (_nameFontFallback is null || _nameFontFallback.Typeface != face || _nameFontFallback.Size != size)
            _nameFontFallback = new SKFont(face, size) { Edging = SKFontEdging.Antialias };
        return _nameFontFallback;
    }

    /// <summary>The gender glyphs as clean vectors: blue male arrow, pink female cross.</summary>
    private static void DrawGender(SKCanvas canvas, SKPoint center, float radius, bool male)
    {
        var color = male ? Pksm.LogoCyan : new SKColor(0xF0, 0x7A, 0x9B);
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2f, radius * 0.32f) };
        if (male)
        {
            canvas.DrawCircle(center.X, center.Y + 1, radius, paint);
            canvas.DrawLine(center.X + radius * 0.7f, center.Y - radius * 0.5f, center.X + radius * 1.5f, center.Y - radius * 1.3f, paint);
            canvas.DrawLine(center.X + radius * 1.5f, center.Y - radius * 1.3f, center.X + radius * 0.75f, center.Y - radius * 1.3f, paint);
            canvas.DrawLine(center.X + radius * 1.5f, center.Y - radius * 1.3f, center.X + radius * 1.5f, center.Y - radius * 0.55f, paint);
        }
        else
        {
            canvas.DrawCircle(center.X, center.Y - 1, radius, paint);
            canvas.DrawLine(center.X, center.Y + radius * 0.75f, center.X, center.Y + radius * 1.9f, paint);
            canvas.DrawLine(center.X - radius * 0.55f, center.Y + radius * 1.3f, center.X + radius * 0.55f, center.Y + radius * 1.3f, paint);
        }
    }
}
