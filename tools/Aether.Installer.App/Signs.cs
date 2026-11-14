using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Aether.Installer.App;

/// <summary>
/// The Monitor's signs, drawn as vectors in Aether's language (the same as Lattice's website): the open cube tinted with one of
/// four gems, three hexagons for the verification level, rhombi for state. No stock icons.
/// </summary>
internal static class Signs
{
    public static readonly Color Ink = Color.Parse("#1A1410"), Mute = Color.Parse("#6B6157"), Line = Color.Parse("#E4DDD3"),
        Chip = Color.Parse("#F2EDE6"), Card0 = Color.Parse("#FFFFFF"), Ground = Color.Parse("#FBFAF8"), Paper = Color.Parse("#F3EDE3"),
        Ember = Color.Parse("#C2500A"), EmberSoft = Color.Parse("#E7A27A"), EmberWash = Color.Parse("#FBE9DD"), Shade = Color.Parse("#F6F2EC");

    /// <summary>Amethyst, jade, amber, garnet: (top face, right face). Jade also means "fine", amber "look", garnet "wrong".</summary>
    private static readonly (Color Top, Color Side)[] Gems =
    {
        (Color.Parse("#7A5C99"), Color.Parse("#B7A3CB")), (Color.Parse("#3E9C8F"), Color.Parse("#9CCEC6")),
        (Color.Parse("#D49A3A"), Color.Parse("#EACB97")), (Color.Parse("#C4514B"), Color.Parse("#E3A3A0")),
    };
    public static Color Jade => Gems[1].Top;
    public static Color Amber => Gems[2].Top;
    public static Color Garnet => Gems[3].Top;

    public static IBrush B(Color c) => new SolidColorBrush(c);

    private static Polygon Face(double[] pts, Color fill, double stroke, Color? strokeColor = null) => new()
    {
        Points = Enumerable.Range(0, pts.Length / 2).Select(i => new Point(pts[i * 2], pts[i * 2 + 1])).ToList(),
        Fill = B(fill), Stroke = B(strokeColor ?? Ink), StrokeThickness = stroke, StrokeJoin = PenLineJoin.Round,
    };

    private static readonly double[] TopFace = { 50, 24, 86, 44, 50, 62, 14, 44 };
    private static readonly double[] LeftFace = { 14, 44, 50, 62, 50, 102, 14, 82 };
    private static readonly double[] RightFace = { 50, 62, 86, 44, 86, 82, 50, 102 };

    /// <summary>The open cube for a mod: gem 1–4 (LatticeDirectory.GemOf), greyed when the mod is switched off.</summary>
    public static Control Cube(double size, int gem, bool on = true)
    {
        var (top, side) = Gems[Math.Clamp(gem, 1, 4) - 1];
        if (!on) { top = Color.Parse("#BDB4A8"); side = Color.Parse("#DDD6CC"); }
        return Box(size, new Control[] { Face(TopFace, top, 2.6), Face(LeftFace, Paper, 2.6), Face(RightFace, side, 2.6) });
    }

    /// <summary>Aether's own cube (ember), for the title bar.</summary>
    public static Control AetherCube(double size) =>
        Box(size, new Control[] { Face(TopFace, Ember, 4), Face(LeftFace, Paper, 4), Face(RightFace, EmberSoft, 4) });

    /// <summary>The install loader: the three faces, each shown as its step is done (0–3).</summary>
    public static (Control View, Action<int> Show) BuildingCube(double size, int gem)
    {
        var (top, side) = Gems[Math.Clamp(gem, 1, 4) - 1];
        var faces = new[] { Face(TopFace, top, 2.2), Face(LeftFace, Paper, 2.2), Face(RightFace, side, 2.2) };
        foreach (var f in faces)
        {
            f.Opacity = 0.12;
            f.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseOut() } };
        }
        return (Box(size, faces), n => { for (int i = 0; i < 3; i++) faces[i].Opacity = i < n ? 1 : 0.12; });
    }

    private static Control Box(double size, Control[] parts)
    {
        var canvas = new Canvas { Width = 100, Height = 100, Margin = new Thickness(0, -8, 0, 0) };
        foreach (var p in parts) canvas.Children.Add(p);
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 100, Height = 100, Children = { canvas } } };
    }

    /// <summary>Verification level: three hexagons filled from the left (listed 1, reproducible 2, reviewed 3).</summary>
    public static Control Hexes(int filled)
    {
        var c = new Canvas { Width = 50, Height = 16 };
        for (int i = 0; i < 3; i++)
        {
            double x = i * 17;
            var hex = new Polygon
            {
                Points = new List<Point> { new(x + 8, 1), new(x + 14, 4.5), new(x + 14, 11.5), new(x + 8, 15), new(x + 2, 11.5), new(x + 2, 4.5) },
                Fill = i < filled ? B(Ember) : Brushes.Transparent, Stroke = B(i < filled ? Ember : Color.Parse("#C9BEAE")), StrokeThickness = 1.4,
            };
            c.Children.Add(hex);
        }
        return new Viewbox { Width = 44, Height = 14, Child = c };
    }

    public static int LevelOf(string level) => level switch { "reviewed" => 3, "reproducible" => 2, "listed" => 1, _ => 0 };

    /// <summary>A rhombus: filled = done or fine, outlined = pending or attention.</summary>
    public static Control Rhombus(Color color, bool filled = true, double size = 12) => new Viewbox
    {
        Width = size, Height = size,
        Child = new Canvas
        {
            Width = 12, Height = 12,
            Children = { new Polygon { Points = new List<Point> { new(6, 1), new(11, 6), new(6, 11), new(1, 6) }, Fill = filled ? B(color) : Brushes.Transparent, Stroke = B(color), StrokeThickness = 1.4 } },
        },
    };

    /// <summary>Section signs for the side bar: Status ◇, My mods (cube), Catalog ⬡, Problems △. Active = filled with ember.</summary>
    public static Control NavSign(string section, bool active)
    {
        if (section == "mods") return active ? AetherCube(16) : OutlineCube(16);
        Color c = active ? Ember : Mute;
        double[] pts = section switch
        {
            "status" => new double[] { 8, 1.5, 14.5, 8, 8, 14.5, 1.5, 8 },
            "catalog" => new double[] { 8, 1, 14, 4.5, 14, 11.5, 8, 15, 2, 11.5, 2, 4.5 },
            _ => new double[] { 8, 2, 15, 14, 1, 14 },
        };
        var poly = new Polygon
        {
            Points = Enumerable.Range(0, pts.Length / 2).Select(i => new Point(pts[i * 2], pts[i * 2 + 1])).ToList(),
            Fill = active ? B(c) : Brushes.Transparent, Stroke = B(c), StrokeThickness = 1.4, StrokeJoin = PenLineJoin.Round,
        };
        return new Viewbox { Width = 16, Height = 16, Child = new Canvas { Width = 16, Height = 16, Children = { poly } } };
    }

    private static Control OutlineCube(double size) => Box(size, new Control[]
    {
        Face(TopFace, Colors.Transparent, 7, Mute), Face(LeftFace, Colors.Transparent, 7, Mute), Face(RightFace, Colors.Transparent, 7, Mute),
    });

    /// <summary>The on/off switch: an ember track and a hexagonal knob that slides. Click toggles; the caller decides what happens.</summary>
    public static Button Switch(bool on, string label, Action onClick)
    {
        var knob = new Polygon
        {
            Points = new List<Point> { new(11, 0), new(22, 5.5), new(22, 16.5), new(11, 22), new(0, 16.5), new(0, 5.5) },
            Fill = Brushes.White, Stroke = B(Color.Parse("#00000022")), StrokeThickness = 1,
            RenderTransform = TransformOperations.Parse(on ? "translateX(20px)" : "translateX(0px)"),
            Transitions = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() } },
        };
        var track = new Border
        {
            Width = 48, Height = 28, CornerRadius = new CornerRadius(14), Background = B(on ? Ember : Color.Parse("#D9D0C3")), Padding = new Thickness(3),
            Child = new Canvas { Width = 42, Height = 22, Children = { knob } },
            Transitions = new Transitions { new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) } },
        };
        var b = new Button { Content = track, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("bare");
        ToolTip.SetTip(b, label);
        Avalonia.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) =>
        {
            // Move first (instant feedback), then let the caller do the file work.
            knob.RenderTransform = TransformOperations.Parse(on ? "translateX(0px)" : "translateX(20px)");
            track.Background = B(on ? Color.Parse("#D9D0C3") : Ember);
            onClick();
        };
        return b;
    }

    /// <summary>A small tag in mono, like Lattice's chips.</summary>
    public static Border Tag(string text, bool strong = false) => new()
    {
        Background = B(strong ? EmberWash : Chip), BorderBrush = B(strong ? EmberSoft : Line), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10.5, Classes = { "mono" }, Foreground = B(strong ? Color.Parse("#A84208") : Ink) },
    };
}
