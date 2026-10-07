using System.Windows;
using CSharpLab.GameEngine;

namespace CSharpLab.Screens;

/// <summary>Linha-guia mostrada quando uma peça encaixa: vertical (X fixo) ou horizontal (Y fixo).</summary>
public readonly record struct Guide(bool Vertical, double Position, double From, double To);

/// <summary>Quais lados da peça estão sendo puxados ao redimensionar.</summary>
[Flags]
public enum Edges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>
/// Faz as peças "grudarem" nas bordas e nos centros das outras peças e do palco, com linhas-guia,
/// como nos editores de design. Sem nada por perto, encaixa numa grade de 8.
/// </summary>
public sealed class Snapper
{
    public const double Grid = 8;

    private readonly List<Rect> _others;

    public Snapper(IEnumerable<Rect> others)
    {
        _others = others.ToList();
    }

    /// <summary>Distância (em pontos do palco) a partir da qual a peça gruda.</summary>
    public double Threshold { get; init; } = 6;

    private IEnumerable<(double Value, double From, double To)> Targets(bool vertical)
    {
        // O palco: bordas, centro e as margens de 40 (um bom respiro nas bordas).
        double size = vertical ? ScreenLayout.Width : ScreenLayout.Height;
        double cross = vertical ? ScreenLayout.Height : ScreenLayout.Width;
        foreach (var v in new[] { 0, 40, size / 2, size - 40, size })
            yield return (v, 0, cross);
        foreach (var r in _others)
        {
            if (vertical)
            {
                yield return (r.Left, r.Top, r.Bottom);
                yield return (r.Left + r.Width / 2, r.Top, r.Bottom);
                yield return (r.Right, r.Top, r.Bottom);
            }
            else
            {
                yield return (r.Top, r.Left, r.Right);
                yield return (r.Top + r.Height / 2, r.Left, r.Right);
                yield return (r.Bottom, r.Left, r.Right);
            }
        }
    }

    /// <summary>Melhor encaixe para algum dos pontos da peça (ex.: esquerda, centro, direita). Null se nada está perto.</summary>
    private (double Delta, double Target, double From, double To)? Best(bool vertical, IEnumerable<double> points)
    {
        (double Delta, double Target, double From, double To)? best = null;
        var targets = Targets(vertical).ToList();
        foreach (var point in points)
        {
            foreach (var (value, from, to) in targets)
            {
                double delta = value - point;
                if (Math.Abs(delta) > Threshold) continue;
                if (best == null || Math.Abs(delta) < Math.Abs(best.Value.Delta) - 0.01)
                    best = (delta, value, from, to);
            }
        }
        return best;
    }

    /// <summary>Encaixe ao mover: a peça inteira anda. <paramref name="snap"/> false só arredonda.</summary>
    public (Rect Rect, List<Guide> Guides) Move(Rect rect, bool snap)
    {
        var guides = new List<Guide>();
        if (!snap) return (Round(rect), guides);

        double x = rect.X, y = rect.Y;
        var bestX = Best(true, [rect.Left, rect.Left + rect.Width / 2, rect.Right]);
        if (bestX is { } bx) x += bx.Delta;
        else x = Math.Round(x / Grid) * Grid;
        var bestY = Best(false, [rect.Top, rect.Top + rect.Height / 2, rect.Bottom]);
        if (bestY is { } by) y += by.Delta;
        else y = Math.Round(y / Grid) * Grid;

        var result = new Rect(x, y, rect.Width, rect.Height);
        if (bestX is { } gx) guides.Add(new Guide(true, gx.Target, Math.Min(gx.From, result.Top), Math.Max(gx.To, result.Bottom)));
        if (bestY is { } gy) guides.Add(new Guide(false, gy.Target, Math.Min(gy.From, result.Left), Math.Max(gy.To, result.Right)));
        return (result, guides);
    }

    /// <summary>Encaixe ao redimensionar: só os lados puxados se mexem.</summary>
    public (Rect Rect, List<Guide> Guides) Resize(Rect rect, Edges edges, bool snap)
    {
        var guides = new List<Guide>();
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        if (snap)
        {
            if (edges.HasFlag(Edges.Left)) left = SnapEdge(true, left, guides);
            if (edges.HasFlag(Edges.Right)) right = SnapEdge(true, right, guides);
            if (edges.HasFlag(Edges.Top)) top = SnapEdge(false, top, guides);
            if (edges.HasFlag(Edges.Bottom)) bottom = SnapEdge(false, bottom, guides);
        }
        var result = new Rect(new Point(Math.Round(left), Math.Round(top)), new Point(Math.Round(right), Math.Round(bottom)));
        for (int i = 0; i < guides.Count; i++)
        {
            var g = guides[i];
            guides[i] = g.Vertical
                ? g with { From = Math.Min(g.From, result.Top), To = Math.Max(g.To, result.Bottom) }
                : g with { From = Math.Min(g.From, result.Left), To = Math.Max(g.To, result.Right) };
        }
        return (result, guides);
    }

    private double SnapEdge(bool vertical, double value, List<Guide> guides)
    {
        if (Best(vertical, [value]) is { } best)
        {
            guides.Add(new Guide(vertical, best.Target, best.From, best.To));
            return best.Target;
        }
        return Math.Round(value / Grid) * Grid;
    }

    private static Rect Round(Rect r) => new(Math.Round(r.X), Math.Round(r.Y), r.Width, r.Height);
}
