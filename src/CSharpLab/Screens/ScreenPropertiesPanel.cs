using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CSharpLab.GameEngine;
using CSharpLab.ViewModels;
using GameColor = CSharpLab.GameEngine.Color;
using GameFont = CSharpLab.GameEngine.Font;
using WpfColor = System.Windows.Media.Color;

namespace CSharpLab.Screens;

/// <summary>
/// Propriedades da peça selecionada (ou da tela, sem seleção). Cada campo muda a tela na hora; digitar
/// num campo vira um passo só no desfazer. Os campos não são recriados quando a tela muda (desfazer,
/// arrastar), então o cursor de quem está digitando nunca se perde.
/// </summary>
public sealed class ScreenPropertiesPanel : Border
{
    private readonly ScreenDesignerModel _model;
    private readonly StackPanel _content = new() { Margin = new Thickness(14, 12, 14, 16) };
    private readonly List<Action> _refreshers = [];
    private string? _shownName;
    private PieceType? _shownType;
    private string? _shownList;
    private int _shownCount;
    private bool _updating;
    private bool _renaming;
    private TextBox? _textField;
    private Action? _refreshCode;

    public ScreenPropertiesPanel(ScreenDesignerModel model)
    {
        _model = model;
        Width = 300;
        BorderThickness = new Thickness(1, 0, 0, 0);
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        SetResourceReference(BackgroundProperty, "BgSidebar");
        Child = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        model.SelectionChanged += Build;
        model.ThemeChanged += OnThemeChanged;
        model.Changed += OnModelChanged;
        // Voltando de outra aba (onde a pessoa pode ter escrito o código da peça): confere de novo.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _refreshCode?.Invoke();
        };
        Build();
    }

    /// <summary>Liga o painel ao código do jogo ("Ao clicar": o que o botão faz e onde está).</summary>
    public MainViewModel? CodeLinks
    {
        get => _codeLinks;
        set
        {
            _codeLinks = value;
            Build();
        }
    }

    private MainViewModel? _codeLinks;

    public void Detach()
    {
        _model.SelectionChanged -= Build;
        _model.ThemeChanged -= OnThemeChanged;
        _model.Changed -= OnModelChanged;
    }

    /// <summary>Põe o cursor no campo de texto da peça (dois cliques ou Enter no palco).</summary>
    public void FocusText()
    {
        if (_textField == null) return;
        _textField.Focus();
        _textField.SelectAll();
    }

    private void OnModelChanged()
    {
        var piece = _model.Selected;
        // Renomear pelo campo Nome continua sendo a mesma peça: só atualiza os valores.
        if (_renaming && piece != null && piece.Type == _shownType) _shownName = piece.Name;
        bool samePiece = piece != null
            ? piece.Name == _shownName && piece.Type == _shownType && piece.List == _shownList
            : _shownName == null && _shownType == null && _model.SelectedNames.Count == _shownCount;
        if (samePiece && _content.Children.Count > 0)
        {
            Refresh();
            if (_renaming) _refreshCode?.Invoke();
        }
        else
        {
            Build();
        }
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            foreach (var refresh in _refreshers) refresh();
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------------ montagem

    private void Build()
    {
        _content.Children.Clear();
        _refreshers.Clear();
        _refreshCode = null;
        _textField = null;
        var piece = _model.Selected;
        _shownName = piece?.Name;
        _shownType = piece?.Type;
        _shownList = piece?.List;
        _shownCount = _model.SelectedNames.Count;

        if (_model.Layout == null)
        {
            _content.Children.Add(Muted("Corrija o erro do arquivo para editar a tela."));
            return;
        }
        if (_model.SelectedNames.Count > 1) BuildMany();
        else if (piece == null) BuildScreen();
        else BuildPiece(piece);
        Refresh();
        _refreshCode?.Invoke();
    }

    /// <summary>
    /// Os quatro temas, cada um com uma miniatura (fundo, painel, título e botão do tema). Um clique escolhe
    /// o tema do jogo inteiro (GameStyle.json).
    /// </summary>
    private FrameworkElement ThemePicker()
    {
        var panel = new StackPanel();
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -6, 0) };
        foreach (var look in Look.All)
        {
            var preview = new Grid { Height = 62 };
            preview.Children.Add(new Border { Background = look.BackgroundBrush, CornerRadius = new CornerRadius(5) });
            var sample = new StackPanel { Margin = new Thickness(8, 3, 8, 4) };
            sample.Children.Add(new TextBlock
            {
                Text = "Aa",
                FontFamily = Theme.FontOf(look.TitleFont),
                FontSize = 17 * Theme.FontScale(look.TitleFont),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(look.Text),
            });
            sample.Children.Add(new Border
            {
                Width = 46,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(Math.Min(look.ButtonRadius, 5)),
                Background = new SolidColorBrush(look.ButtonDefault),
                Margin = new Thickness(0, 2, 0, 0),
            });
            preview.Children.Add(new Border
            {
                Margin = new Thickness(8),
                Background = new SolidColorBrush(look.Panel),
                BorderBrush = new SolidColorBrush(look.PanelBorder),
                BorderThickness = new Thickness(look.PanelBorders ? 1 : 0),
                CornerRadius = new CornerRadius(Math.Min(look.PanelRadius, 8)),
                Child = sample,
            });

            var name = new TextBlock { Text = look.Title, FontSize = 12, Margin = new Thickness(2, 4, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var content = new StackPanel();
            content.Children.Add(preview);
            content.Children.Add(name);

            bool chosen = _model.Look.Name == look.Name;
            var tile = new Border
            {
                Child = content,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = look.Description,
            };
            if (chosen) tile.SetResourceReference(Border.BorderBrushProperty, "Accent");
            else tile.BorderBrush = Brushes.Transparent;
            tile.MouseEnter += (_, _) => { if (!chosen) tile.SetResourceReference(Border.BackgroundProperty, "BgHover"); };
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            var theme = look.Name;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (theme == _model.Look.Name) return;
                if (_model.SetTheme(theme) is { } problem) MessageBox.Show(Window.GetWindow(this)!, problem, "Tema do jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            System.Windows.Automation.AutomationProperties.SetName(tile, "Tema " + look.Title);
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);

        var hint = Muted("Vale para todas as telas do jogo. O que você escolher numa peça (cor, fonte…) continua valendo.");
        hint.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(hint);
        if (_model.ThemeError is { } error)
        {
            var problem = Muted(error + " Por enquanto, a tela usa o Clássico.");
            problem.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            problem.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(problem);
        }
        return panel;
    }

    /// <summary>O tema mudou: as cores e fontes do painel (bolinhas de cor, letras) acompanham.</summary>
    private void OnThemeChanged() => Build();

    private void BuildScreen()
    {
        _content.Children.Add(Title("Tela", $"cena \"{_model.SceneName}\""));

        if (_model.ProjectDirectory != null)
        {
            _content.Children.Add(Section("TEMA DO JOGO"));
            _content.Children.Add(ThemePicker());
        }

        _content.Children.Add(Section("FUNDO"));
        _content.Children.Add(ImagePicker(() => _model.Layout?.Background, image => _model.SetBackground(image), "Sem imagem de fundo"));

        _content.Children.Add(Section("NO CÓDIGO"));
        if (CodeLinks != null && _model.ProjectDirectory != null)
        {
            _content.Children.Add(SceneCodeField());
        }
        else
        {
            _content.Children.Add(Muted("Esta tela aparece quando o jogo entra na cena com este nome:"));
            _content.Children.Add(CodeBox($"game.Scene(\"{_model.SceneName}\", () =>\n{{\n    game.Find(\"Nome\")...\n}});"));
        }

        _content.Children.Add(Section("DICAS"));
        foreach (var tip in new[]
                 {
                     "Clique numa peça para mudar texto, cor e tamanho.",
                     "Shift+clique (ou arrastar um retângulo no fundo) seleciona várias peças: elas andam juntas.",
                     "Ctrl+C numa tela e Ctrl+V em outra cola no mesmo lugar e com os mesmos nomes.",
                     "Arraste os quadradinhos dos cantos para redimensionar. Shift mantém a proporção.",
                     "As peças grudam nas outras e nas bordas. Segure Alt para soltar livre.",
                     "Setas movem 1 (Shift: 10). Ctrl+D duplica, Del apaga, Ctrl+Z desfaz.",
                     "A tela é um arquivo de texto: para ver, use Arquivo, lá em cima.",
                 })
        {
            _content.Children.Add(Bullet(tip));
        }

        if (_model.Warnings.Count > 0)
        {
            _content.Children.Add(Section("AVISOS DO ARQUIVO"));
            foreach (var warning in _model.Warnings.Take(6)) _content.Children.Add(Bullet(warning, "WarningBrush"));
        }
    }

    /// <summary>Várias peças selecionadas: o que dá para fazer com todas de uma vez.</summary>
    private void BuildMany()
    {
        var pieces = _model.SelectedPieces;
        _content.Children.Add(Title($"{pieces.Count} peças", "selecionadas juntas"));
        _content.Children.Add(Section("PEÇAS"));
        var names = new TextBlock { Text = string.Join(", ", pieces.Select(p => p.Name)), TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)FindResource("CodeFont"), FontSize = 12 };
        names.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _content.Children.Add(names);

        _content.Children.Add(Section("FAZER COM TODAS"));
        foreach (var (text, action) in new (string, Action)[]
                 {
                     ("Duplicar  (Ctrl+D)", () => _model.Duplicate()),
                     ("Copiar  (Ctrl+C)", _model.Copy),
                     ("Apagar  (Del)", _model.Delete),
                 })
        {
            var button = new Button { Content = text, Style = (Style)FindResource("DialogButton"), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch, Height = 30, Focusable = false };
            button.Click += (_, _) => action();
            _content.Children.Add(button);
        }

        _content.Children.Add(Section("DICAS"));
        foreach (var tip in new[]
                 {
                     "Arraste qualquer uma: todas andam juntas. As setas também.",
                     "Copie aqui e cole em outra tela (Ctrl+V): as peças vão para o mesmo lugar, com os mesmos nomes.",
                     "Shift+clique numa peça tira ela da seleção.",
                 })
        {
            _content.Children.Add(Bullet(tip));
        }
    }

    private void BuildPiece(Piece piece)
    {
        var name = piece.Name;
        var type = piece.Type;
        Piece? Current() => _model.Layout?.Find(_shownName ?? name);
        void Edit(Action<Piece> change, string key) { if (_shownName != null) _model.Edit(_shownName, change, key + ":" + _shownName); }

        var header = Title(Piece.Describe(type), null);
        var delete = new Button { Content = "", Style = (Style)FindResource("IconButton"), ToolTip = "Apagar peça (Del)" };
        delete.Click += (_, _) => _model.Delete();
        DockPanel.SetDock(delete, Dock.Right);
        ((DockPanel)header).Children.Insert(0, delete);
        _content.Children.Add(header);

        if (_model.Layout?.ListOf(piece) is { } owner)
        {
            var info = Bullet($"Faz parte do cartão da lista \"{owner.Name}\": aparece uma vez para cada item. No código, use card.Find(\"{name}\") dentro do Show.", "Accent");
            info.Margin = new Thickness(0, 6, 0, 0);
            _content.Children.Add(info);
        }

        _content.Children.Add(Section("NOME"));
        _content.Children.Add(NameField());

        if (Piece.Supports(type, nameof(Piece.Text)))
        {
            _content.Children.Add(Section(type switch
            {
                PieceType.Bar => "RÓTULO",
                PieceType.Input => "PERGUNTA",
                PieceType.List => "QUANDO ESTIVER VAZIA",
                _ => "TEXTO",
            }));
            var field = TextField(() => Current()?.Text ?? "", v => Edit(p => p.Text = v, "text"), multiline: type == PieceType.Text);
            _textField = field;
            _content.Children.Add(field);

            // Texto que não cabe: o aviso aparece logo embaixo, com o conserto ao lado.
            if (type is PieceType.Text or PieceType.Button) _content.Children.Add(OverflowWarning(Current));
            if (Piece.Supports(type, nameof(Piece.Scroll)))
            {
                var scroll = SwitchRow("Rolagem", "Ligado: texto comprido ganha uma barra de rolagem no jogo, em vez de ser cortado.",
                    () => Current()?.Scroll == true, v => Edit(p => p.Scroll = v ? true : null, "scroll"));
                scroll.Margin = new Thickness(0, 10, 0, 0);
                _content.Children.Add(scroll);
            }
        }

        // Botão e campo de escrita: logo depois do texto, o que eles fazem (fica no código; o painel mostra onde).
        bool showsHandler = type is PieceType.Button or PieceType.Input && CodeLinks != null && _model.ProjectDirectory != null;
        if (showsHandler)
        {
            _content.Children.Add(Section(type == PieceType.Button ? "AO CLICAR" : "AO RESPONDER"));
            _content.Children.Add(HandlerField(type == PieceType.Button ? "OnClick" : "OnAnswer", inCard: piece.List != null));
        }

        if (type == PieceType.Button)
        {
            _content.Children.Add(Section("ESTILO"));
            _content.Children.Add(Segmented(
                [(ButtonStyle.Filled, "Cheio", "Fundo colorido: a ação principal (Atacar)"),
                 (ButtonStyle.Outline, "Contorno", "Só a borda: ações secundárias"),
                 (ButtonStyle.Text, "Só texto", "Sem fundo nem borda: ações discretas (Voltar)")],
                () => Current()?.Style ?? ButtonStyle.Filled, v => Edit(p => p.Style = v == ButtonStyle.Filled ? null : v, "style")));
        }

        if (type == PieceType.List)
        {
            _content.Children.Add(Section("CARTÃO DE CADA ITEM"));
            var card = new UniformGrid { Columns = 2 };
            card.Children.Add(Labeled("Largura", NumberField(() => Current()?.CardW ?? 180, v => Edit(p => p.CardWidth = v, "cardw"), 24, 960)));
            card.Children.Add(Labeled("Altura", NumberField(() => Current()?.CardH ?? 220, v => Edit(p => p.CardHeight = v, "cardh"), 24, 540), left: 8));
            card.Children.Add(Labeled("Espaço entre eles", NumberField(() => Current()?.CardGap ?? 16, v => Edit(p => p.Gap = v, "gap"), 0, 200), top: 8));
            _content.Children.Add(card);
            var how = Muted("Ponha as peças de um item (nome, preço, botão…) dentro do primeiro cartão, o tracejado. " +
                            "O jogo repete esse cartão para cada item da lista; se não couber, aparece uma rolagem.");
            how.Margin = new Thickness(0, 8, 0, 0);
            _content.Children.Add(how);
        }

        if (type == PieceType.Bar)
        {
            var row = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(Labeled("Valor", NumberField(() => Current()?.BarValue ?? 0, v => Edit(p => p.Value = (int)v, "value"), 0, 99999)));
            row.Children.Add(Labeled("Máximo", NumberField(() => Current()?.BarMax ?? 100, v => Edit(p => p.Max = (int)v, "max"), 1, 99999), left: 8));
            _content.Children.Add(row);
        }

        if (type == PieceType.Image)
        {
            _content.Children.Add(Section("IMAGEM"));
            _content.Children.Add(ImagePicker(() => Current()?.Image, image => Edit(p => p.Image = image, "image"), "Escolher imagem…"));
        }

        if (Piece.Supports(type, nameof(Piece.Size)) || Piece.Supports(type, nameof(Piece.Bold)))
        {
            _content.Children.Add(Section("LETRA"));
            if (Piece.Supports(type, nameof(Piece.Font)))
            {
                var fonts = FontField(() => Current()?.Font ?? GameFont.Normal, v => Edit(p => p.Font = v == GameFont.Normal ? null : v, "font"));
                fonts.Margin = new Thickness(0, 0, 0, 8);
                _content.Children.Add(fonts);
            }
            var row = new DockPanel();
            if (Piece.Supports(type, nameof(Piece.Size)))
            {
                var size = NumberField(() => Current()?.FontSize ?? 20, v => Edit(p => p.Size = v, "size"), 8, 120);
                size.Width = 64;
                size.ToolTip = "Tamanho da letra (roda do mouse muda)";
                DockPanel.SetDock(size, Dock.Left);
                row.Children.Add(size);
            }
            if (Piece.Supports(type, nameof(Piece.Bold)))
            {
                var bold = Toggle("B", "Negrito", () => Current()?.Bold == true, v => Edit(p => p.Bold = v ? true : null, "bold"), bold: true);
                bold.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(bold, Dock.Left);
                row.Children.Add(bold);
                var italic = Toggle("I", "Itálico", () => Current()?.Italic == true, v => Edit(p => p.Italic = v ? true : null, "italic"), bold: false);
                italic.FontStyle = FontStyles.Italic;
                italic.FontFamily = new FontFamily("Georgia");
                italic.Margin = new Thickness(2, 0, 0, 0);
                DockPanel.SetDock(italic, Dock.Left);
                row.Children.Add(italic);
                var align = AlignField(() => Current()?.Align ?? TextAlign.Left, v => Edit(p => p.Align = v == TextAlign.Left ? null : v, "align"));
                align.HorizontalAlignment = HorizontalAlignment.Right;
                row.Children.Add(align);
            }
            else
            {
                row.Children.Add(new Border());
            }
            _content.Children.Add(row);
            if (Piece.Supports(type, nameof(Piece.Shadow)))
            {
                var shadow = SwitchRow("Sombra nas letras", "Uma sombra escura atrás do texto: fica legível em cima de qualquer fundo.",
                    () => Current()?.Shadow == true, v => Edit(p => p.Shadow = v ? true : null, "shadow"));
                shadow.Margin = new Thickness(0, 10, 0, 0);
                _content.Children.Add(shadow);
            }
        }

        if (Piece.Supports(type, nameof(Piece.Color)))
        {
            _content.Children.Add(Section("COR"));
            _content.Children.Add(ColorField(() => Current()?.Color, v => Edit(p => p.Color = v, "color")));
            var shade = Segmented(
                [(Shade.Normal, "Normal", "A cor como ela é"), (Shade.Dark, "Escuro", "A cor mais escura (vermelho escuro, azul escuro…)"), (Shade.Light, "Claro", "A cor mais clarinha")],
                () => Current()?.Shade ?? Shade.Normal, v => Edit(p => p.Shade = v == Shade.Normal ? null : v, "shade"));
            shade.Margin = new Thickness(0, 6, 0, 0);
            _content.Children.Add(shade);
        }

        if (type == PieceType.Box)
        {
            _content.Children.Add(Section("PREENCHIMENTO"));
            var fill = new DockPanel();
            var percent = new TextBlock { Text = "%  (0 = invisível, 100 = cheia)", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            percent.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var opacity = NumberField(() => Current()?.BoxOpacity ?? 100, v => Edit(p => p.Opacity = (int)v, "opacity"), 0, 100);
            opacity.Width = 64;
            opacity.ToolTip = "Roda do mouse muda (Shift: de 10 em 10)";
            DockPanel.SetDock(opacity, Dock.Left);
            fill.Children.Add(opacity);
            fill.Children.Add(percent);
            _content.Children.Add(fill);
            var border = SwitchRow("Borda", "Uma linha em volta da caixa.", () => Current()?.HasBorder == true, v => Edit(p => p.Border = v, "border"));
            border.Margin = new Thickness(0, 12, 0, 0);
            _content.Children.Add(border);
            _content.Children.Add(Section("CANTOS"));
            _content.Children.Add(Segmented(
                [(Corner.Round, "Redondos", "Cantos arredondados"), (Corner.Square, "Retos", "Cantos em ângulo"), (Corner.Circle, "Círculo", "Círculo (numa caixa quadrada) ou oval: retratos redondos")],
                () => Current()?.Corner ?? Corner.Round, v => Edit(p => p.Corner = v == Corner.Round ? null : v, "corner")));
        }

        _content.Children.Add(Section(piece.List != null ? "POSIÇÃO NO CARTÃO E TAMANHO" : "POSIÇÃO E TAMANHO"));
        var bounds = new UniformGrid { Columns = 2 };
        bounds.Children.Add(Labeled("X", NumberField(() => Current()?.X ?? 0, v => Edit(p => p.X = v, "x"), -2000, 3000)));
        bounds.Children.Add(Labeled("Y", NumberField(() => Current()?.Y ?? 0, v => Edit(p => p.Y = v, "y"), -2000, 3000), left: 8));
        bounds.Children.Add(Labeled("Largura", NumberField(() => Current()?.Width ?? 0, v => Edit(p => p.Width = v, "w"), ScreenDesignerModel.MinSize, 3000), top: 8));
        bounds.Children.Add(Labeled("Altura", NumberField(() => Current()?.Height ?? 0, v => Edit(p => p.Height = v, "h"), ScreenDesignerModel.MinSize, 3000), left: 8, top: 8));
        _content.Children.Add(bounds);

        var visible = SwitchRow("Aparece no começo", "Desligado: a peça começa escondida e o código mostra com .Visible = true.",
            () => Current()?.Visible != false, v => Edit(p => p.Visible = v, "visible"));
        visible.Margin = new Thickness(0, 14, 0, 0);
        _content.Children.Add(visible);
        if (Piece.Supports(type, nameof(Piece.Enabled)))
        {
            var enabled = SwitchRow("Ativo no começo", "Desligado: começa apagado e sem clique; o código liga com .Enabled = true.",
                () => Current()?.Enabled != false, v => Edit(p => p.Enabled = v, "enabled"));
            enabled.Margin = new Thickness(0, 10, 0, 0);
            _content.Children.Add(enabled);
        }

        if (showsHandler) return;   // o código dele já aparece em "Ao clicar"

        _content.Children.Add(Section("NO CÓDIGO"));
        var code = CodeBox(ScreenDesignerModel.CodeExample(piece));
        _refreshers.Add(() =>
        {
            if (Current() is { } p && code.Tag is TextBlock text) text.Text = ScreenDesignerModel.CodeExample(p);
        });
        _content.Children.Add(code);
    }

    // ------------------------------------------------------------------ campos

    /// <summary>
    /// O que o botão (ou o campo de escrita) faz: "Já faz algo — Program.cs, linha 34" com o caminho até lá,
    /// ou "Ainda não faz nada" com um atalho que escreve a estrutura vazia na cena certa.
    /// </summary>
    private FrameworkElement HandlerField(string handler, bool inCard)
    {
        bool isButton = handler == "OnClick";
        var card = new CodeStatusCard(this, isButton ? "Escrever o que ele faz" : "Escrever o que acontece");
        PieceCode? found = null;
        card.LinkClicked += () =>
        {
            if (found != null) CodeLinks?.GoToPieceCode(found);
        };
        card.ActionClicked += () =>
        {
            if (CodeLinks != null && _model.ProjectDirectory is { } dir && _shownName is { } name)
                CodeLinks.WritePieceHandler(dir, _model.SceneName, name, handler);
        };

        _refreshCode = () =>
        {
            if (CodeLinks == null || _model.ProjectDirectory is not { } dir || _shownName is not { } name) return;
            found = CodeLinks.FindPieceCode(dir, _model.SceneName, name, handler);
            string where = found != null ? $"{System.IO.Path.GetFileName(found.File)}, linha {found.Line}" : "";
            string snippet = isButton ? $"game.Find(\"{name}\").OnClick(() => {{ }});" : $"game.Find(\"{name}\").OnAnswer(answer => {{ }});";
            if (found is { HasHandler: true })
            {
                card.Show(ok: true, isButton ? "Já faz algo" : "Já responde", where, showAction: false,
                    isButton ? "O que acontece no clique está no código, dentro do OnClick."
                             : "O que acontece com a resposta está no código, dentro do OnAnswer.");
            }
            else if (inCard)
            {
                // Botão de cartão: o OnClick vai dentro do Show da Lista (um para cada item), não solto na cena.
                card.Show(ok: false, isButton ? "Ainda não faz nada" : "Ainda não faz nada com a resposta",
                    found != null ? $"Usado em {where}" : null, showAction: false,
                    $"Dentro do Show da lista, escreva card.Find(\"{name}\").{handler}(...): cada cartão ganha o seu.");
            }
            else
            {
                card.Show(ok: false, isButton ? "Ainda não faz nada" : "Ainda não faz nada com a resposta",
                    found != null ? $"Usado em {where}" : null, showAction: true,
                    $"Escreve {snippet} na cena \"{_model.SceneName}\". O que acontece, você escreve entre as chaves.");
            }
        };
        return card.Element;
    }

    /// <summary>A cena no código: "game.Scene("Fight") — Program.cs, linha 30", ou o atalho para escrever a cena.</summary>
    private FrameworkElement SceneCodeField()
    {
        var card = new CodeStatusCard(this, "Escrever a cena no código");
        PieceCode? found = null;
        card.LinkClicked += () =>
        {
            if (found != null) CodeLinks?.GoToPieceCode(found);
        };
        card.ActionClicked += () =>
        {
            if (CodeLinks != null && _model.ProjectDirectory is { } dir) CodeLinks.GoToSceneCode(dir, _model.SceneName);
        };
        _refreshCode = () =>
        {
            if (CodeLinks == null || _model.ProjectDirectory is not { } dir) return;
            found = CodeLinks.FindSceneCode(dir, _model.SceneName);
            var scene = $"game.Scene(\"{_model.SceneName}\")";
            if (found != null)
                card.Show(ok: true, scene, $"{System.IO.Path.GetFileName(found.File)}, linha {found.Line}", showAction: false,
                    "É ali que as peças desta tela ganham vida, com game.Find(\"Nome\").");
            else
                card.Show(ok: false, "Esta cena ainda não está no código", null, showAction: true,
                    $"Sem {scene}, o jogo não consegue entrar nesta tela.");
        };
        return card.Element;
    }

    /// <summary>
    /// Um cartãozinho de situação (✓ tudo certo / ⚠ falta algo), com um link até o código e um botão de atalho.
    /// </summary>
    private sealed class CodeStatusCard
    {
        private readonly TextBlock _icon = new() { FontSize = 13, Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        private readonly Button _link;
        private readonly Button _action;
        private readonly TextBlock _hint;

        public event Action? LinkClicked;
        public event Action? ActionClicked;

        public CodeStatusCard(FrameworkElement owner, string actionText)
        {
            _icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            _link = new Button { Style = (Style)owner.FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, -4), FontSize = 12, ToolTip = "Ir até o código" };
            _link.Click += (_, _) => LinkClicked?.Invoke();
            var text = new StackPanel();
            text.Children.Add(_status);
            text.Children.Add(_link);
            var row = new DockPanel();
            DockPanel.SetDock(_icon, Dock.Left);
            row.Children.Add(_icon);
            row.Children.Add(text);
            var card = new Border { Child = row, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "BgBase");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            _action = new Button
            {
                Content = actionText,
                Style = (Style)owner.FindResource("DialogButton"),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 30,
                Focusable = false,
            };
            _action.Click += (_, _) => ActionClicked?.Invoke();
            _hint = Muted("");
            _hint.Margin = new Thickness(0, 6, 0, 0);

            var panel = new StackPanel();
            panel.Children.Add(card);
            panel.Children.Add(_action);
            panel.Children.Add(_hint);
            Element = panel;
        }

        public FrameworkElement Element { get; }

        public void Show(bool ok, string status, string? link, bool showAction, string hint)
        {
            _icon.Text = ok ? "" : "";
            _icon.SetResourceReference(TextBlock.ForegroundProperty, ok ? "SuccessBrush" : "WarningBrush");
            _status.Text = status;
            _link.Content = link;
            _link.Visibility = link != null ? Visibility.Visible : Visibility.Collapsed;
            _action.Visibility = showAction ? Visibility.Visible : Visibility.Collapsed;
            _hint.Text = hint;
        }
    }

    /// <summary>
    /// "O texto não cabe": quanto falta e um botão que aumenta a peça até caber (o Texto também pode ganhar rolagem).
    /// Some sozinho quando o texto passa a caber.
    /// </summary>
    private FrameworkElement OverflowWarning(Func<Piece?> current)
    {
        var icon = new TextBlock { Text = "!", FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Child = icon, Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        badge.SetResourceReference(Border.BackgroundProperty, "WarningBrush");
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var fit = new Button { Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 2, 0, -4), FontSize = 12 };
        var text = new StackPanel();
        text.Children.Add(message);
        text.Children.Add(fit);
        var row = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(text);
        var card = new Border { Child = row, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "BgBase");
        card.SetResourceReference(Border.BorderBrushProperty, "WarningBrush");

        fit.Click += (_, _) =>
        {
            if (_shownName != null) _model.FitToText(_shownName);
        };
        _refreshers.Add(() =>
        {
            var piece = current();
            var overflow = piece != null ? ScreenRenderer.Overflow(piece) : null;
            card.Visibility = overflow != null ? Visibility.Visible : Visibility.Collapsed;
            if (piece == null || overflow == null) return;
            if (piece.Type == PieceType.Button)
            {
                message.Text = $"O texto não cabe no botão: no jogo ele termina em \"…\". Faltam {Math.Ceiling(overflow.Width - piece.Width)} de largura.";
                fit.Content = "Aumentar a largura até caber";
            }
            else
            {
                message.Text = $"O texto não cabe: no jogo, o fim dele fica cortado. Faltam {Math.Ceiling(overflow.Height - piece.Height)} de altura.";
                fit.Content = "Ajustar a altura ao texto";
            }
        });
        return card;
    }

    private FrameworkElement NameField()
    {
        var box = new TextBox { FontFamily = (FontFamily)FindResource("CodeFont") };
        var error = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        var hint = Muted("É o nome usado no game.Find. Em inglês, sem espaço nem acento.");
        hint.Margin = new Thickness(0, 4, 0, 0);

        void Commit()
        {
            string? problem;
            _renaming = true;
            try
            {
                problem = _model.Rename(box.Text);
            }
            finally
            {
                _renaming = false;
            }
            error.Text = problem ?? "";
            error.Visibility = problem != null ? Visibility.Visible : Visibility.Collapsed;
            hint.Visibility = problem != null ? Visibility.Collapsed : Visibility.Visible;
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = _model.SelectedName ?? "";
                error.Visibility = Visibility.Collapsed;
                hint.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        _refreshers.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin) box.Text = _model.SelectedName ?? "";
        });

        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(error);
        panel.Children.Add(hint);
        return panel;
    }

    private TextBox TextField(Func<string> get, Action<string> set, bool multiline)
    {
        var box = new TextBox
        {
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 58 : 0,
            MaxHeight = 160,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        box.TextChanged += (_, _) =>
        {
            if (_updating) return;
            set(box.Text.Replace("\r\n", "\n"));
        };
        box.LostKeyboardFocus += (_, _) => _model.EndMerge();
        _refreshers.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin) box.Text = get();
        });
        return box;
    }

    /// <summary>Número: digitar, setas ↑↓ ou a roda do mouse (Shift: de 10 em 10).</summary>
    private TextBox NumberField(Func<double> get, Action<double> set, double min, double max)
    {
        var box = new TextBox { HorizontalContentAlignment = HorizontalAlignment.Left };
        string Format(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        void Apply(double value)
        {
            value = Math.Clamp(Math.Round(value), min, max);
            set(value);
            if (box.IsKeyboardFocusWithin)
            {
                _updating = true;
                box.Text = Format(value);
                box.CaretIndex = box.Text.Length;
                _updating = false;
            }
        }
        bool TryRead(out double value) =>
            double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        box.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (TryRead(out var v) && v >= min && v <= max)
            {
                box.ClearValue(TextBox.BorderBrushProperty);
                set(Math.Round(v));
            }
            else if (box.Text.Trim().Length > 0)
            {
                box.SetResourceReference(TextBox.BorderBrushProperty, "ErrorBrush");
            }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            if (e.Key is Key.Up or Key.Down)
            {
                Apply((TryRead(out var v) ? v : get()) + (e.Key == Key.Up ? step : -step));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _model.EndMerge();
                e.Handled = true;
            }
        };
        box.MouseWheel += (_, e) =>
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            Apply(get() + (e.Delta > 0 ? step : -step));
            e.Handled = true;
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            _model.EndMerge();
            box.ClearValue(TextBox.BorderBrushProperty);
            _updating = true;
            box.Text = Format(get());
            _updating = false;
        };
        _refreshers.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin) box.Text = Format(get());
        });
        return box;
    }

    private static readonly GameColor[] Palette =
        [GameColor.White, GameColor.Gray, GameColor.Red, GameColor.Orange, GameColor.Gold, GameColor.Green, GameColor.Blue, GameColor.Purple];

    private static readonly Dictionary<GameColor, string> ColorNames = new()
    {
        [GameColor.White] = "Branco (White)", [GameColor.Gray] = "Cinza (Gray)", [GameColor.Red] = "Vermelho (Red)",
        [GameColor.Orange] = "Laranja (Orange)", [GameColor.Gold] = "Dourado (Gold)", [GameColor.Green] = "Verde (Green)",
        [GameColor.Blue] = "Azul (Blue)", [GameColor.Purple] = "Roxo (Purple)",
    };

    /// <summary>Bolinhas de cor; a primeira é a cor padrão da peça.</summary>
    private FrameworkElement ColorField(Func<GameColor?> get, Action<GameColor?> set)
    {
        var panel = new WrapPanel();
        var swatches = new List<(GameColor? Color, Border Ring)>();
        foreach (var color in new GameColor?[] { null }.Concat(Palette.Select(c => (GameColor?)c)))
        {
            FrameworkElement dot;
            if (color is { } c)
            {
                var rgb = Theme.Rgb(c);
                dot = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(rgb) };
            }
            else
            {
                // "Padrão": círculo vazio cortado por uma linha.
                var grid = new Grid { Width = 18, Height = 18 };
                grid.Children.Add(new Ellipse { Stroke = Theme.Muted, StrokeThickness = 1.2 });
                grid.Children.Add(new Line { X1 = 4, Y1 = 14, X2 = 14, Y2 = 4, Stroke = Theme.Muted, StrokeThickness = 1.2 });
                dot = grid;
            }
            var ring = new Border
            {
                Child = dot,
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(0, 0, 4, 4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = color is { } n ? ColorNames[n] : "Cor padrão",
            };
            ring.MouseLeftButtonUp += (_, _) => set(color);
            swatches.Add((color, ring));
            panel.Children.Add(ring);
        }
        _refreshers.Add(() =>
        {
            var current = get();
            foreach (var (color, ring) in swatches)
            {
                if (color == current) ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
                else ring.BorderBrush = Brushes.Transparent;
            }
        });
        return panel;
    }

    private FrameworkElement AlignField(Func<TextAlign> get, Action<TextAlign> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<(TextAlign Align, ToggleButton Button)>();
        foreach (var (align, glyph, tip) in new[] { (TextAlign.Left, "", "À esquerda"), (TextAlign.Center, "", "No centro"), (TextAlign.Right, "", "À direita") })
        {
            var button = new ToggleButton
            {
                Content = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                Style = (Style)FindResource("IconToggle"),
                ToolTip = tip,
                Focusable = false,
                Margin = new Thickness(2, 0, 0, 0),
            };
            button.Click += (_, _) => set(align);
            buttons.Add((align, button));
            panel.Children.Add(button);
        }
        _refreshers.Add(() =>
        {
            var current = get();
            foreach (var (align, button) in buttons) button.IsChecked = align == current;
        });
        return panel;
    }

    /// <summary>Botões lado a lado, um aceso: Cheio | Contorno | Só texto, Normal | Escuro | Claro…</summary>
    private FrameworkElement Segmented<T>(IReadOnlyList<(T Value, string Text, string Tip)> options, Func<T> get, Action<T> set) where T : struct
    {
        var grid = new UniformGrid { Rows = 1 };
        var buttons = new List<(T Value, ToggleButton Button)>();
        foreach (var (value, text, tip) in options)
        {
            var button = new ToggleButton
            {
                Content = text,
                Style = (Style)FindResource("IconToggle"),
                ToolTip = tip,
                Focusable = false,
                FontSize = 12,
                Height = 28,
                Margin = new Thickness(0, 0, 3, 0),
            };
            button.Width = double.NaN;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => set(value);
            buttons.Add((value, button));
            grid.Children.Add(button);
        }
        _refreshers.Add(() =>
        {
            var current = get();
            foreach (var (value, button) in buttons) button.IsChecked = EqualityComparer<T>.Default.Equals(value, current);
        });
        return grid;
    }

    /// <summary>As 4 fontes, cada nome escrito na própria fonte (assim dá para ver antes de escolher).</summary>
    private FrameworkElement FontField(Func<GameFont> get, Action<GameFont> set)
    {
        var grid = new UniformGrid { Columns = 2 };
        var buttons = new List<(GameFont Font, ToggleButton Button)>();
        foreach (var (font, text, tip) in new[]
                 {
                     (GameFont.Normal, "Normal", "Font.Normal: limpa e fácil de ler"),
                     (GameFont.Fantasy, "Fantasia", "Font.Fantasy: de conto de fadas, boa para títulos"),
                     (GameFont.Book, "Livro", "Font.Book: de livro antigo, boa para pergaminhos e histórias"),
                     (GameFont.Hand, "À mão", "Font.Hand: parece escrita com caneta (bilhetes, diários)"),
                 })
        {
            var button = new ToggleButton
            {
                Content = new TextBlock { Text = text, FontFamily = Theme.FontOf(font), FontSize = 13 * Theme.FontScale(font) },
                Style = (Style)FindResource("IconToggle"),
                ToolTip = tip,
                Focusable = false,
                Height = 30,
                Margin = new Thickness(0, 0, 3, 3),
            };
            button.Width = double.NaN;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => set(font);
            buttons.Add((font, button));
            grid.Children.Add(button);
        }
        _refreshers.Add(() =>
        {
            var current = get();
            foreach (var (font, button) in buttons) button.IsChecked = font == current;
        });
        return grid;
    }

    private ToggleButton Toggle(string text, string tip, Func<bool> get, Action<bool> set, bool bold)
    {
        var button = new ToggleButton
        {
            Content = text,
            Style = (Style)FindResource("IconToggle"),
            ToolTip = tip,
            Focusable = false,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 13,
        };
        button.Click += (_, _) => set(button.IsChecked == true);
        _refreshers.Add(() => button.IsChecked = get());
        return button;
    }

    /// <summary>Interruptor liga/desliga (no tema escuro do editor).</summary>
    private FrameworkElement SwitchRow(string label, string tip, Func<bool> get, Action<bool> set)
    {
        var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9) };
        var knob = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0) };
        track.Child = knob;
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var row = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
        DockPanel.SetDock(track, Dock.Right);
        row.Children.Add(track);
        row.Children.Add(text);

        bool value = false;
        void Show()
        {
            knob.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            if (value) track.SetResourceReference(Border.BackgroundProperty, "Accent");
            else track.SetResourceReference(Border.BackgroundProperty, "BgActive");
        }
        row.MouseLeftButtonUp += (_, _) =>
        {
            value = !value;
            Show();
            set(value);
        };
        _refreshers.Add(() =>
        {
            value = get();
            Show();
        });
        return row;
    }

    /// <summary>Escolher uma imagem da pasta Assets (ou trazer uma nova para lá).</summary>
    private FrameworkElement ImagePicker(Func<string?> get, Action<string?> set, string emptyText)
    {
        var current = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        current.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var chevron = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var content = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Right);
        content.Children.Add(chevron);
        content.Children.Add(current);
        var pick = new Button
        {
            Content = content,
            Style = (Style)FindResource("DialogButton"),
            Margin = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 10, 0),
            Height = 30,
            Focusable = false,
        };
        pick.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = pick, Placement = PlacementMode.Bottom, MinWidth = pick.ActualWidth };
            var none = new MenuItem { Header = "(nenhuma)" };
            none.Click += (_, _) => set(null);
            menu.Items.Add(none);
            var files = _model.ImageFiles();
            if (files.Count > 0) menu.Items.Add(new Separator());
            foreach (var file in files)
            {
                var item = new MenuItem { Header = file, IsChecked = string.Equals(file, get(), StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => set(file);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var add = new MenuItem { Header = "Adicionar imagem do computador…" };
            add.Click += (_, _) =>
            {
                if (ImportImage() is { } imported) set(imported);
            };
            menu.Items.Add(add);
            var open = new MenuItem { Header = "Abrir a pasta Assets" };
            open.Click += (_, _) => OpenAssetsFolder();
            menu.Items.Add(open);
            menu.IsOpen = true;
        };
        _refreshers.Add(() =>
        {
            var value = get();
            current.Text = string.IsNullOrEmpty(value) ? emptyText : value;
            current.Opacity = string.IsNullOrEmpty(value) ? 0.6 : 1;
        });
        return pick;
    }

    private string? ImportImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolher imagem (ela será copiada para a pasta Assets do jogo)",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return null;
        try
        {
            return _model.ImportImage(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Não foi possível copiar a imagem: " + ex.Message, "Imagem", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void OpenAssetsFolder()
    {
        if (_model.AssetsDirectory is not { } dir) return;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Core.Settings.AppPaths.Log(ex, "Abrindo Assets");
        }
    }

    // ------------------------------------------------------------------ textos e caixas

    private static FrameworkElement Title(string text, string? detail)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var stack = new StackPanel();
        var title = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        stack.Children.Add(title);
        if (detail != null)
        {
            var sub = new TextBlock { Text = detail, FontSize = 12 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            stack.Children.Add(sub);
        }
        panel.Children.Add(stack);
        return panel;
    }

    private static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        return block;
    }

    private static FrameworkElement Bullet(string text, string brush = "TextSecondary")
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var dot = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 6, 0) };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        DockPanel.SetDock(dot, Dock.Left);
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        row.Children.Add(dot);
        row.Children.Add(block);
        return row;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement field, double left = 0, double top = 0)
    {
        var panel = new StackPanel { Margin = new Thickness(left, top, 0, 0) };
        var text = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 3) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        panel.Children.Add(text);
        panel.Children.Add(field);
        return panel;
    }

    /// <summary>Um pedaço de código para copiar, mostrando como usar a peça no Program.cs.</summary>
    private FrameworkElement CodeBox(string code)
    {
        var text = new TextBlock
        {
            Text = code,
            FontFamily = (FontFamily)FindResource("CodeFont"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var copy = new Button { Content = "Copiar", Style = (Style)FindResource("LinkButton"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, -4, -4) };
        copy.Click += (_, _) =>
        {
            ScreenStage.TrySetClipboard(text.Text.Replace("\n", Environment.NewLine));
            copy.Content = "Copiado!";
        };
        copy.MouseLeave += (_, _) => copy.Content = "Copiar";
        var stack = new StackPanel();
        stack.Children.Add(text);
        stack.Children.Add(copy);
        var box = new Border
        {
            Child = stack,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Tag = text,
        };
        box.SetResourceReference(Border.BackgroundProperty, "BgBase");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return box;
    }
}
