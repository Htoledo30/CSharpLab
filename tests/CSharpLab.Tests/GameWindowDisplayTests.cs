using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSharpLab.GameEngine;

namespace CSharpLab.Tests;

public sealed class GameWindowDisplayTests
{
    [Fact]
    public void Ao_sair_da_tela_cheia_restaura_tamanho_e_posicao_da_janela() => OnSta(() =>
    {
        var window = new GameWindow(new Game("Janela"))
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 100, Top = 100, Width = 800, Height = 500,
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            var before = window.RestoreBounds;
            window.HandleKey(Key.F11, ModifierKeys.None, false, false);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.HandleKey(Key.F11, ModifierKeys.None, false, false);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(before, window.RestoreBounds);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void F11_entra_em_tela_cheia_e_restaura_o_estado_anterior(WindowState initialState) => OnSta(() =>
    {
        var window = new GameWindow(new Game("Janela")) { WindowState = initialState };
        try
        {
            var style = window.WindowStyle;
            var resize = window.ResizeMode;
            Assert.True(window.HandleKey(Key.F11, ModifierKeys.None, false, false));
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            // Segurar F11 não fica entrando e saindo de tela cheia.
            Assert.True(window.HandleKey(Key.F11, ModifierKeys.None, true, false));
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.HandleKey(Key.F11, ModifierKeys.None, false, false));
            Assert.Equal(initialState, window.WindowState);
            Assert.Equal(style, window.WindowStyle);
            Assert.Equal(resize, window.ResizeMode);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void F11_funciona_durante_digitacao_e_ignora_combinacoes() => OnSta(() =>
    {
        var window = new GameWindow(new Game("Janela"));
        try
        {
            Assert.False(window.HandleKey(Key.F11, ModifierKeys.Control, false, true));
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
            Assert.True(window.HandleKey(Key.F11, ModifierKeys.None, false, true));
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.HandleKey(Key.F11, ModifierKeys.None, false, true));
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]
    [InlineData(2560, 1080)]
    public void Cena_preserva_proporcao_em_diferentes_formatos(double width, double height) => OnSta(() =>
    {
        var window = new GameWindow(new Game("Janela"));
        try
        {
            var viewbox = Assert.IsType<Viewbox>(window.Content);
            viewbox.Measure(new Size(width, height));
            viewbox.Arrange(new Rect(0, 0, width, height));
            var stage = Assert.IsType<Grid>(viewbox.Child);
            var bounds = stage.TransformToAncestor(viewbox).TransformBounds(new Rect(stage.RenderSize));
            Assert.Equal(16d / 9, bounds.Width / bounds.Height, 5);
            Assert.True(bounds.Width <= width + 1);
            Assert.True(bounds.Height <= height + 1);
            Assert.Equal(Stretch.Uniform, viewbox.Stretch);
            Assert.Same(stage.Background, window.Background);
        }
        finally { window.Close(); }
    });

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) Assert.Fail(failure.ToString());
    }
}
