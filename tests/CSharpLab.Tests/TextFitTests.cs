using System.Windows.Controls;
using CSharpLab.GameEngine;

namespace CSharpLab.Tests;

/// <summary>Texto que não cabe: o motor mede igual ao jogo, e a peça Texto pode rolar em vez de cortar.</summary>
public sealed class TextFitTests
{
    private const string Long =
        "Encostado numa árvore, um velho segura a perna machucada. Uma bolsa pesada está ao lado dele, " +
        "e o vento traz o cheiro de chuva. Lá longe, a torre do Rei Goblin brilha em vermelho.";

    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure?.ToString());
    }

    [Fact]
    public void Mede_quanto_falta_para_o_texto_caber() => OnSta(() =>
    {
        var story = new Piece { Type = PieceType.Text, Name = "Story", Width = 400, Height = 40, Text = Long, Size = 18 };
        var fit = ScreenRenderer.Overflow(story)!;
        Assert.Equal(400, fit.Width);
        Assert.True(fit.Height >= 75, $"precisa de {fit.Height}");   // umas 4 linhas

        // Com a altura que ele pediu, cabe.
        story.Height = fit.Height;
        Assert.Null(ScreenRenderer.Overflow(story));

        // Com rolagem, nunca é cortado.
        story.Height = 40;
        story.Scroll = true;
        Assert.Null(ScreenRenderer.Overflow(story));

        // Fonte maior precisa de mais espaço.
        var title = new Piece { Type = PieceType.Text, Name = "Title", Width = 200, Height = 40, Text = "A Coroa Perdida", Size = 40, Font = Font.Fantasy };
        Assert.NotNull(ScreenRenderer.Overflow(title));

        // Botão: o texto fica numa linha só, então o que falta é largura.
        var button = new Piece { Type = PieceType.Button, Name = "Buy", Width = 120, Height = 48, Text = "Comprar a espada de aço" };
        var wide = ScreenRenderer.Overflow(button)!;
        Assert.True(wide.Width > 120);
        Assert.Equal(48, wide.Height);
        button.Width = wide.Width;
        Assert.Null(ScreenRenderer.Overflow(button));

        // Outras peças e texto vazio não têm aviso.
        Assert.Null(ScreenRenderer.Overflow(new Piece { Type = PieceType.Box, Name = "B", Width = 10, Height = 10, Text = Long }));
        Assert.Null(ScreenRenderer.Overflow(new Piece { Type = PieceType.Text, Name = "E", Width = 10, Height = 10, Text = "" }));
    });

    [Fact]
    public void Texto_com_rolagem_vira_uma_area_que_rola() => OnSta(() =>
    {
        var story = new Piece { Type = PieceType.Text, Name = "Story", Width = 400, Height = 60, Text = Long, Scroll = true };
        var element = ScreenRenderer.Create(story, new RenderContext { Live = true });
        Assert.IsType<ScrollViewer>(((Border)element).Child);

        story.Scroll = null;
        element = ScreenRenderer.Create(story, new RenderContext { Live = true });
        Assert.IsType<TextBlock>(((Border)element).Child);
    });

    [Fact]
    public void Rolagem_no_arquivo_da_tela_e_no_codigo()
    {
        var parsed = ScreenFile.Parse("""{ "pieces": [ { "type": "Text", "name": "Letter", "x": 0, "y": 0, "width": 300, "height": 80, "text": "Querido herói...", "scroll": true } ] }""");
        Assert.True(parsed.Success, parsed.Error);
        Assert.Empty(parsed.Warnings);
        Assert.True(parsed.Layout!.Find("Letter")!.Scroll);
        Assert.Contains("\"scroll\": true", ScreenFile.Serialize(parsed.Layout));

        // Pelo código, só no Texto; nas outras peças o erro explica.
        var item = new Item(new Piece { Type = PieceType.Text, Name = "Letter" }, "Start") { Scroll = true };
        Assert.True(item.Scroll);
        var error = Assert.Throws<GameException>(() => new Item(new Piece { Type = PieceType.Button, Name = "Buy" }, "Start").Scroll = true);
        Assert.Contains("Scroll", error.Message);
    }
}
