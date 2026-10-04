#!/usr/bin/env python3
"""Builds PKForge's color themes from the artist's mockups.

Each mockup in mockups/ is the same two-screen composition (PC box and editor panel on
top, Pokémon summary below) painted in one theme; default.png is the app's own blues.
Because the compositions are identical, comparing a theme with default.png pixel by pixel
gives, for every flat interface color of the default, the color the artist chose for it in
that theme. Sprites, type plates and the "Legal" tag keep their colors in every mockup, so
the same comparison also tells them apart: those pixels map to themselves.

Every chrome color the app paints is a role below, with its exact default value, so the
default theme stays pixel-identical to the app before themes. A role's value in another
theme is the mockup's mapping of its default color: taken as is when the default color is
in the mockup, otherwise blended from the nearest mapped colors (in Lab space, weighted by
inverse squared distance), so colors the mockup never shows still follow the theme.

Usage:
    python3 tools/Themes/build.py

Writes:
    src/PKForge.Chrome/ColorThemes.g.cs
"""
import math
import os
import sys
from collections import Counter, defaultdict

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
MOCKUPS = os.path.join(HERE, "mockups")
OUT = os.path.join(ROOT, "src", "PKForge.Chrome", "ColorThemes.g.cs")

# Theme order in the picker: the default, then the types in the games' type order.
THEMES = [
    ("default", "Default"), ("normal", "Normal"), ("fighting", "Fighting"), ("flying", "Flying"),
    ("poison", "Poison"), ("ground", "Ground"), ("rock", "Rock"), ("bug", "Bug"), ("ghost", "Ghost"),
    ("steel", "Steel"), ("fire", "Fire"), ("water", "Water"), ("grass", "Grass"), ("electric", "Electric"),
    ("psychic", "Psychic"), ("ice", "Ice"), ("dragon", "Dragon"), ("dark", "Dark"), ("fairy", "Fairy"),
]

# (role, default value, what it paints). The default values are the app's colors before themes.
ROLES = [
    # The PKSM logo palette every page is built from.
    ("Void", "14121D", "Darkest navy: outlines, shadows, scrims, ink on an accent."),
    ("Deep", "171B32", "Recessed navy: pressed buttons, wells, alternate rows."),
    ("Deck", "1B2447", "Panel body: page background and window fill."),
    ("Grid", "2B4E95", "Cobalt: panel edges, header strips, selection fill."),
    ("Blue", "2789CD", "Button blue and the default accent."),
    ("Cyan", "42BFE8", "Focus cyan: focus rims, key discs, icons."),
    ("Ink", "F4F8FF", "Main text."),
    ("InkSoft", "A8BADC", "Secondary text."),
    ("Bright", "FFFFFF", "Pure white chrome: banner captions, highlights, sparkles."),
    # The storage box and its header.
    ("Well", "041F46", "The box well, and the base of panels."),
    ("WellEdge", "08376E", "The well's rim."),
    ("Frame", "1B2346", "The box header's frame."),
    ("FrameEdge", "23569F", "The box header frame's rim, and panel rims."),
    ("BannerTop", "738AB8", "The box name banner's light top."),
    ("BannerBottom", "255396", "The box name banner's dark bottom."),
    ("PoolLight", "78C8FF", "The cursor's light pool."),
    ("WallpaperMid", "0E3368", "The Blue box background's middle tone."),
    ("WallpaperLight", "1E5892", "The Blue box background's light tone."),
    ("PointerRim", "081434", "The storage pointer's outline."),
    ("PointerPale", "C8ECFF", "The storage pointer's pale face."),
    # The editor panel.
    ("Label", "2360B0", "Editor captions."),
    ("Value", "C6D2EE", "Editor values."),
    ("Rim", "16B6DC", "The editor and summary rim cyan."),
    ("RimFill", "042856", "The species tab's fill."),
    ("SubInk", "607EBA", "The EXP line and small notes."),
    ("ChipInk", "D2F0FF", "Section chip text and focused captions."),
    ("ChipTop", "144682", "Section chip and focus gradient, top."),
    ("ChipBottom", "08285A", "Section chip and focus gradient, bottom."),
    ("Band", "02112B", "Row bands under the editor fields."),
    ("ToolFill", "25539A", "Tool buttons."),
    ("ToolEdge", "4C7CC4", "Tool button rims, and the move category plate's light half."),
    ("CategoryDark", "1E407C", "The move category plate's dark half."),
    # The summary and the Pokédex page.
    ("PanelFill", "04244E", "The summary panel."),
    ("LabelColumn", "061939", "The summary's label column."),
    ("LabelInk", "284682", "Summary captions and card rims."),
    ("ValueInk", "96A8D2", "Summary values."),
    ("CardFill", "0C142C", "The species card."),
    ("CardRow", "192447", "The species card's rows."),
    ("SpeciesFill", "1089B6", "The species tab."),
    ("SpeciesEdge", "5AD2F0", "The species tab's rim."),
    ("SpeciesInk", "C8F0FF", "The species tab's text."),
    ("NameFrame", "1D2244", "The name banner's frame."),
    ("Shadow", "060814", "Drop shadows under panels and cards."),
    ("TabTop", "1E4886", "The tab strip, top."),
    ("TabBottom", "103064", "The tab strip, bottom."),
    ("TabActiveTop", "7E9CD6", "The active tab, top."),
    ("TabActiveBottom", "2C5AA6", "The active tab, bottom."),
    ("TabIcon", "F0F6FF", "Tab icons."),
    ("GlowHigh", "5096E6", "Glow lines, light."),
    ("GlowMid", "286EC8", "Glow lines, middle."),
    ("GlowLow", "143C82", "Glow lines, dark."),
    ("RadarFill", "7896D2", "The stats hexagon's base-stat fill."),
    ("RadarInk", "F0F8FF", "The stats hexagon's line and dots."),
    ("DexNumber", "607EBA", "Pokédex numbers."),
    ("Silhouette", "020C22", "Unseen Pokémon's silhouettes."),
    ("DexCard", "0A1634", "The Bank's Pokédex entry card."),
    # The party screen.
    ("PartyGridTop", "060C1C", "The party grid's dark top."),
    ("PartyRim", "6CDEF6", "The party panels' bright rim and trace."),
    ("PartyRimDark", "0C1A34", "The party panels' dark outer line."),
    ("PartyBody", "2A4C7E", "A party panel."),
    ("PartyBand", "365E96", "A party panel's light band."),
    ("PartySelected", "2E6CB8", "The selected party panel."),
    ("PartySelectedBand", "3C82CC", "The selected party panel's light band."),
    ("PartyShadow", "081024", "The party's text shadow."),
]


def hexrgb(value):
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def lab(rgb):
    def lin(c):
        c /= 255
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(c) for c in rgb)
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883

    def f(t):
        return t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(x), f(y), f(z)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def mapping(default, theme):
    """The color each default color becomes in this theme, for colors flat enough to trust:
    at least 800 pixels and an answer for most of them (antialiased edges have neither). White
    is the reason it is a majority, not near-unanimity: text recolors with the theme while the
    sprites' white stays white."""
    by = defaultdict(Counter)
    for a, b in zip(default, theme):
        by[a][b] += 1
    result = {}
    for src, answers in by.items():
        total = sum(answers.values())
        dst, n = answers.most_common(1)[0]
        if total >= 800 and n / total >= 0.5:
            result[src] = dst
    return result


def main():
    pixels = {}
    for theme, _ in THEMES:
        path = os.path.join(MOCKUPS, f"{theme}.png")
        if not os.path.exists(path):
            sys.exit(f"missing mockup: {path}")
        pixels[theme] = list(Image.open(path).convert("RGB").get_flattened_data()
                             if hasattr(Image.Image, "get_flattened_data")
                             else Image.open(path).convert("RGB").getdata())
    maps = {theme: mapping(pixels["default"], pixels[theme]) for theme, _ in THEMES if theme != "default"}

    # Chrome sources: colors every theme maps and at least one theme recolors (sprites, type
    # plates and the Legal tag map to themselves everywhere and must not steer chrome).
    shared = set.intersection(*(set(m) for m in maps.values()))
    sources = [s for s in shared if any(m[s] != s for m in maps.values())]
    labs = {s: lab(s) for s in sources}

    palettes = {}
    report = []
    for theme, _ in THEMES:
        palette = {}
        for role, default, _ in ROLES:
            rgb = hexrgb(default)
            if theme == "default":
                palette[role] = rgb
                continue
            target = lab(rgb)
            near = sorted(sources, key=lambda s: math.dist(labs[s], target))[:4]
            nearest = math.dist(labs[near[0]], target)
            if nearest < 2.0:
                palette[role] = maps[theme][near[0]]
            else:
                weights = [1 / max(math.dist(labs[s], target), 1e-6) ** 2 for s in near]
                total = sum(weights)
                palette[role] = tuple(round(sum(w * maps[theme][s][i] for w, s in zip(weights, near)) / total)
                                      for i in range(3))
            if theme == "fire":
                report.append((role, default, round(nearest, 1)))
        palettes[theme] = palette

    write(palettes)
    far = [r for r in report if r[2] >= 12]
    print(f"{len(sources)} chrome colors in the mockups; {len(ROLES)} roles; {len(THEMES)} themes.")
    print("roles blended from colors at Lab distance >= 12 (check these against the mockups):")
    for role, default, distance in far:
        print(f"  {role} #{default} ({distance})")


def write(palettes):
    lines = [
        "// <auto-generated>",
        "// Built by tools/Themes/build.py from the artist's mockups in tools/Themes/mockups.",
        "// Do not edit: change the roles or the mockups and run the script again.",
        "// </auto-generated>",
        "using SkiaSharp;",
        "",
        "namespace PKForge.Chrome;",
        "",
        "public sealed partial record ColorTheme",
        "{",
    ]
    for role, _, doc in ROLES:
        lines.append(f"    /// <summary>{doc}</summary>")
        lines.append(f"    public required SKColor {role} {{ get; init; }}")
    lines.append("}")
    lines.append("")
    lines.append("public static partial class ColorThemes")
    lines.append("{")
    lines.append("    /// <summary>Every theme, in the picker's order: the default, then the types.</summary>")
    lines.append("    public static readonly IReadOnlyList<ColorTheme> All =")
    lines.append("    [")
    for theme, name in THEMES:
        lines.append(f"        new ColorTheme")
        lines.append("        {")
        lines.append(f"            Id = \"{theme}\",")
        lines.append(f"            Name = \"{name}\",")
        for role, _, _ in ROLES:
            r, g, b = palettes[theme][role]
            lines.append(f"            {role} = new SKColor(0x{r:02X}, 0x{g:02X}, 0x{b:02X}),")
        lines.append("        },")
    lines.append("    ];")
    lines.append("}")
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
