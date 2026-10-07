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
using GameColor = CSharpLab.GameEngine.Color;
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
    private bool _updating;
    private bool _renaming;
    private TextBox? _textField;

    public ScreenPropertiesPanel(ScreenDesignerModel model)
    {
        _model = model;
        Width = 300;
        BorderThickness = new Thickness(1, 0, 0, 0);
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        SetResourceReference(BackgroundProperty, "BgSidebar");
        Child = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        model.SelectionChanged += Build;
        model.Changed += OnModelChanged;
        Build();
    }

    public void Detach()
    {
        _model.SelectionChanged -= Build;
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
        bool samePiece = piece != null ? piece.Name == _shownName && piece.Type == _shownType : _shownName == null && _shownType == null;
        if (samePiece && _content.Children.Count > 0) Refresh();
        else Build();
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
        _textField = null;
        var piece = _model.Selected;
        _shownName = piece?.Name;
        _shownType = piece?.Type;

        if (_model.Layout == null)
        {
            _content.Children.Add(Muted("Corrija o erro do arquivo para editar a tela."));
            return;
        }
        if (piece == null) BuildScreen();
        else BuildPiece(piece);
        Refresh();
    }

    private void BuildScreen()
    {
        _content.Children.Add(Title("Tela", $"cena \"{_model.SceneName}\""));

        _content.Children.Add(Section("FUNDO"));
        _content.Children.Add(ImagePicker(() => _model.Layout?.Background, image => _model.SetBackground(image), "Sem imagem de fundo"));

        _content.Children.Add(Section("NO CÓDIGO"));
        _content.Children.Add(Muted("Esta tela aparece quando o jogo entra na cena com este nome:"));
        _content.Children.Add(CodeBox($"game.Scene(\"{_model.SceneName}\", () =>\n{{\n    game.Find(\"Nome\")...\n}});"));

        _content.Children.Add(Section("DICAS"));
        foreach (var tip in new[]
                 {
                     "Clique numa peça para mudar texto, cor e tamanho.",
                     "Arraste os quadradinhos dos cantos para redimensionar. Shift mantém a proporção.",
                     "As peças grudam nas outras e nas bordas. Segure Alt para soltar livre.",
                     "Setas movem 1 (Shift: 10). Ctrl+D duplica, Del apaga, Ctrl+Z desfaz.",
                     "Para ver ou editar o arquivo, use Texto, lá em cima.",
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

        _content.Children.Add(Section("NOME"));
        _content.Children.Add(NameField());

        if (Piece.Supports(type, nameof(Piece.Text)))
        {
            _content.Children.Add(Section(type switch
            {
                PieceType.Bar => "RÓTULO",
                PieceType.Input => "PERGUNTA",
                _ => "TEXTO",
            }));
            var field = TextField(() => Current()?.Text ?? "", v => Edit(p => p.Text = v, "text"), multiline: type == PieceType.Text);
            _textField = field;
            _content.Children.Add(field);
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
                var align = AlignField(() => Current()?.Align ?? TextAlign.Left, v => Edit(p => p.Align = v == TextAlign.Left ? null : v, "align"));
                align.HorizontalAlignment = HorizontalAlignment.Right;
                row.Children.Add(align);
            }
            else
            {
                row.Children.Add(new Border());
            }
            _content.Children.Add(row);
        }

        if (Piece.Supports(type, nameof(Piece.Color)))
        {
            _content.Children.Add(Section("COR"));
            _content.Children.Add(ColorField(() => Current()?.Color, v => Edit(p => p.Color = v, "color")));
        }

        _content.Children.Add(Section("POSIÇÃO E TAMANHO"));
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

        _content.Children.Add(Section("NO CÓDIGO"));
        var code = CodeBox(ScreenDesignerModel.CodeExample(piece));
        _refreshers.Add(() =>
        {
            if (Current() is { } p && code.Tag is TextBlock text) text.Text = ScreenDesignerModel.CodeExample(p);
        });
        _content.Children.Add(code);
    }

    // ------------------------------------------------------------------ campos

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
