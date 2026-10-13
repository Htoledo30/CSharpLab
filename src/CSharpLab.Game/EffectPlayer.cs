using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CSharpLab.GameEngine;

/// <summary>
/// Os movimentos das peças: tremer, piscar e o texto do Pop. A janela do jogo e o Estúdio (o "▶ Ver" do painel)
/// usam os mesmos, então o que aparece no Estúdio é igual ao jogo.
/// </summary>
internal static class EffectPlayer
{
    /// <summary>Quanto tempo o texto do Pop fica na tela, do começo até sumir.</summary>
    public static readonly TimeSpan PopTime = TimeSpan.FromMilliseconds(1050);

    /// <summary>Tremer (vai e volta para os lados) ou piscar (some e volta duas vezes). Começa quando a peça aparece.</summary>
    public static void Play(FrameworkElement element, Effect effect)
    {
        if (effect == Effect.Shake)
        {
            var move = new TranslateTransform();
            element.RenderTransform = move;
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            double[] steps = [0, -9, 9, -7, 7, -4, 4, -2, 0];
            for (int i = 0; i < steps.Length; i++)
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420.0 * i / (steps.Length - 1)))));
            WhenShown(element, () => move.BeginAnimation(TranslateTransform.XProperty, shake));
            return;
        }
        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
        double[] values = [1, 0.15, 1, 0.15, 1];
        for (int i = 0; i < values.Length; i++)
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460.0 * i / (values.Length - 1)))));
        var opacity = element.Opacity;
        flash.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = opacity;
        };
        WhenShown(element, () => element.BeginAnimation(UIElement.OpacityProperty, flash));
    }

    /// <summary>
    /// O texto do Pop, por cima de tudo em <paramref name="host"/>: aparece no meio da peça crescendo um pouco,
    /// sobe e some. Sem a peça desenhada (escondida), usa <paramref name="fallback"/>, o lugar dela na tela.
    /// <paramref name="index"/>: vários Pop na mesma peça saem um depois do outro, um pouco para os lados.
    /// </summary>
    public static void Pop(Canvas host, FrameworkElement? element, Rect fallback, string text, Color? color, int index = 0)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = Theme.FontOf(Font.Strong),
            FontSize = 30,
            Foreground = Theme.Brush(color ?? Color.White),
            Effect = PopShadow,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var scale = new ScaleTransform(1, 1);
        var rise = new TranslateTransform();
        label.RenderTransform = new TransformGroup { Children = { scale, rise } };
        label.Opacity = 0;
        host.Children.Add(label);

        void Start()
        {
            var at = element != null && element.IsLoaded && PresentationSource.FromVisual(element) != null
                ? element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), host)
                : new Point(fallback.X + fallback.Width / 2, fallback.Y + fallback.Height / 2);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = double.IsNaN(host.Width) ? host.ActualWidth : host.Width;
            double height = double.IsNaN(host.Height) ? host.ActualHeight : host.Height;
            double shift = index == 0 ? 0 : (index % 2 == 1 ? 1 : -1) * 18 * ((index + 1) / 2);
            Canvas.SetLeft(label, Math.Clamp(at.X - label.DesiredSize.Width / 2 + shift, 0, Math.Max(0, width - label.DesiredSize.Width)));
            Canvas.SetTop(label, Math.Clamp(at.Y - label.DesiredSize.Height / 2, 0, Math.Max(0, height - label.DesiredSize.Height)));

            var delay = TimeSpan.FromMilliseconds(170 * index);
            var grow = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
            grow.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            grow.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            rise.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -48, PopTime) { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(PopTime)));
            fade.Completed += (_, _) => host.Children.Remove(label);
            label.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // A posição da peça só existe depois que ela aparece na janela.
        if (element != null && !element.IsLoaded) element.Loaded += (_, _) => Start();
        else WhenShown(label, Start);
    }

    /// <summary>Quanto a roleta gira até parar.</summary>
    public static readonly TimeSpan SpinTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A roleta gira algumas voltas, de onde estava (<paramref name="fromAngle"/>) até onde ela está agora, freando
    /// no fim. A roleta guarda o giro do disco no Tag (feito pelo desenho dela).
    /// </summary>
    public static void Spin(FrameworkElement wheel, double fromAngle)
    {
        if (wheel.Tag is not RotateTransform rotate) return;
        double to = rotate.Angle;
        double turn = ((to - fromAngle) % 360 + 360) % 360;
        double from = to - (turn + 360 * 4);
        var spin = new DoubleAnimation(from, to, SpinTime)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        WhenShown(wheel, () => rotate.BeginAnimation(RotateTransform.AngleProperty, spin));
    }

    /// <summary>Roda já, se o elemento já está na tela; senão, quando ele aparecer.</summary>
    private static void WhenShown(FrameworkElement element, Action start)
    {
        if (element.IsLoaded) start();
        else element.Loaded += (_, _) => start();
    }

    private static readonly DropShadowEffect PopShadow = CreatePopShadow();

    private static DropShadowEffect CreatePopShadow()
    {
        var shadow = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2, BlurRadius = 5, Opacity = 0.95, Direction = 300 };
        shadow.Freeze();
        return shadow;
    }
}
