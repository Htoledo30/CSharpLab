using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpLab.Editor;
using CSharpLab.ViewModels;

namespace CSharpLab.Screens;

/// <summary>
/// A aba de um arquivo Screens/*.json: a tela para arrastar (Tela) ou o arquivo em si (Texto).
/// As duas mostram o mesmo documento, então mudar numa aparece na outra.
/// </summary>
public sealed class ScreenEditorView : Grid
{
    private readonly DocumentViewModel _doc;
    private readonly MainViewModel _vm;
    private readonly Grid _design = new();
    private readonly Border _textHost = new() { Visibility = Visibility.Collapsed };
    private readonly Border _errorCard;
    private readonly TextBlock _errorText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 14) };
    private readonly ToggleButton _designButton;
    private readonly ToggleButton _textButton;
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly Button _codeButton;
    private CodeEditor? _editor;

    public ScreenEditorView(DocumentViewModel doc, MainViewModel vm)
    {
        _doc = doc;
        _vm = vm;
        Model = new ScreenDesignerModel(doc);
        Stage = new ScreenStage(Model);
        Properties = new ScreenPropertiesPanel(Model);
        Palette = new PiecePalette(Model);
        SetResourceReference(BackgroundProperty, "BgEditor");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Cabeçalho: nome da tela, desfazer/refazer e a troca Tela | Texto.
        var header = new DockPanel { Height = 38, LastChildFill = true };
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 8, 0) };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        headerBorder.SetResourceReference(Border.BackgroundProperty, "BgSidebar");

        _designButton = ModeButton("Tela", "Montar a tela arrastando as peças");
        _textButton = ModeButton("Texto", "Ver e editar o arquivo da tela (JSON)");
        _designButton.Click += (_, _) => ShowDesign();
        _textButton.Click += (_, _) => ShowText();
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(_designButton);
        modes.Children.Add(_textButton);
        var modesBorder = new Border { Child = modes, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        modesBorder.SetResourceReference(Border.BackgroundProperty, "BgBase");
        DockPanel.SetDock(modesBorder, Dock.Right);
        header.Children.Add(modesBorder);

        _undo = IconButton("", "Desfazer (Ctrl+Z)", Model.Undo);
        _redo = IconButton("", "Refazer (Ctrl+Y)", Model.Redo);
        _redo.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(_redo, Dock.Right);
        DockPanel.SetDock(_undo, Dock.Right);
        header.Children.Add(_redo);
        header.Children.Add(_undo);

        _codeButton = CodeButton();
        DockPanel.SetDock(_codeButton, Dock.Right);
        header.Children.Add(_codeButton);

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        title.Inlines.Add(new System.Windows.Documents.Run("Tela  "));
        var scene = new System.Windows.Documents.Run($"cena \"{Model.SceneName}\"") { FontWeight = FontWeights.Normal };
        scene.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextMuted");
        title.Inlines.Add(scene);
        header.Children.Add(title);
        Children.Add(headerBorder);

        // Corpo: peças | palco | propriedades
        _design.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _design.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _design.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _design.Children.Add(Palette);
        SetColumn(Stage, 1);
        _design.Children.Add(Stage);
        SetColumn(Properties, 2);
        _design.Children.Add(Properties);

        _errorCard = CreateErrorCard();
        SetColumn(_errorCard, 1);
        SetColumnSpan(_errorCard, 2);
        _design.Children.Add(_errorCard);

        SetRow(_design, 1);
        SetRow(_textHost, 1);
        Children.Add(_design);
        Children.Add(_textHost);

        Stage.EditTextRequested += Properties.FocusText;
        Palette.PieceAdded += FocusStage;
        Model.Changed += UpdateState;
        Model.Renamed += OnRenamed;
        doc.Document.UndoStack.PropertyChanged += OnUndoStackChanged;
        SizeChanged += (_, e) => FitPanels(e.NewSize.Width);
        UpdateState();
        UpdateModeButtons();
    }

    public ScreenDesignerModel Model { get; }
    public ScreenStage Stage { get; }
    public ScreenPropertiesPanel Properties { get; }
    public PiecePalette Palette { get; }

    public bool IsDesignMode { get; private set; } = true;

    /// <summary>O editor de texto, quando a aba Texto está aberta (para buscar, desfazer pelo menu etc.).</summary>
    public CodeEditor? ActiveTextEditor => IsDesignMode ? null : _editor;

    private static ToggleButton ModeButton(string text, string tip)
    {
        var button = new ToggleButton
        {
            Content = text,
            ToolTip = tip,
            Padding = new Thickness(12, 2, 12, 3),
            Height = 26,
            Focusable = false,
            Cursor = Cursors.Hand,
        };
        button.SetResourceReference(StyleProperty, "IconToggle");
        button.Width = double.NaN;
        return button;
    }

    private Button IconButton(string glyph, string tip, Action action)
    {
        var button = new Button { Content = glyph, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IconButton");
        button.Click += (_, _) =>
        {
            action();
            Stage.Focus();
        };
        return button;
    }

    /// <summary>"Código da cena": vai até o game.Scene("Nome", …) no código.</summary>
    private Button CodeButton()
    {
        var icon = new TextBlock { Text = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = "Código da cena", VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content,
            ToolTip = $"Ir para o game.Scene(\"{Model.SceneName}\", …) no código, onde as peças ganham vida com game.Find",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
        };
        button.SetResourceReference(StyleProperty, "TextButton");
        button.Visibility = Model.ProjectDirectory != null ? Visibility.Visible : Visibility.Collapsed;
        button.Click += (_, _) =>
        {
            if (Model.ProjectDirectory is { } dir) _vm.GoToSceneCode(dir, Model.SceneName);
        };
        return button;
    }

    private Border CreateErrorCard()
    {
        var icon = new TextBlock { Text = "", FontSize = 22 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        var title = new TextBlock { Text = "A tela não pode ser mostrada", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var open = new Button { Content = "Corrigir no Texto", HorizontalAlignment = HorizontalAlignment.Left };
        open.SetResourceReference(StyleProperty, "PrimaryButton");
        open.Margin = new Thickness(0);
        open.Click += (_, _) =>
        {
            ShowText();
            if (Model.ErrorLine is { } line && _editor != null)
                Dispatcher.BeginInvoke(() => _editor.GoTo(line, 1, null), DispatcherPriority.Loaded);
        };
        var undo = new Button { Content = "Desfazer a última mudança" };
        undo.SetResourceReference(StyleProperty, "DialogButton");
        undo.Click += (_, _) => Model.Undo();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(open);
        buttons.Children.Add(undo);

        var stack = new StackPanel { MaxWidth = 460 };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(_errorText);
        stack.Children.Add(buttons);
        var card = new Border
        {
            Child = stack,
            Padding = new Thickness(24, 20, 24, 20),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrong");
        // O fundo atrás do cartão cobre o palco inteiro.
        var backdrop = new Border { Child = card, Visibility = Visibility.Collapsed };
        backdrop.SetResourceReference(Border.BackgroundProperty, "BgBase");
        return backdrop;
    }

    /// <summary>
    /// Em janelas estreitas, as peças viram só desenhos e as propriedades encolhem um pouco, para o palco
    /// não ficar minúsculo. Uma folga entre os limites evita ficar trocando ao redimensionar devagar.
    /// </summary>
    private void FitPanels(double width)
    {
        if (width < 1000) Palette.Compact = true;
        else if (width > 1040) Palette.Compact = false;
        Properties.Width = width < 860 ? 272 : 300;
    }

    private void UpdateState()
    {
        bool broken = Model.Layout == null;
        _errorCard.Visibility = broken ? Visibility.Visible : Visibility.Collapsed;
        Palette.IsEnabled = !broken;
        if (broken)
        {
            var line = Model.ErrorLine is { } l ? $"Linha {l}: " : "";
            _errorText.Text = line + Model.Error;
        }
        UpdateUndoButtons();
    }

    /// <summary>O código que usava o nome antigo passa a usar o novo (game.Find da mesma cena).</summary>
    private void OnRenamed(string oldName, string newName)
    {
        if (Model.ProjectDirectory == null) return;
        int changed = _vm.RenamePieceInCode(Model.ProjectDirectory, Model.SceneName, oldName, newName);
        if (changed > 0)
            _vm.NotifyInfo(changed == 1
                ? $"O game.Find(\"{oldName}\") do código também mudou para \"{newName}\" (ainda não salvo)."
                : $"{changed} game.Find(\"{oldName}\") do código também mudaram para \"{newName}\" (ainda não salvos).");
    }

    private void OnUndoStackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateUndoButtons();

    private void UpdateUndoButtons()
    {
        _undo.IsEnabled = Model.CanUndo;
        _redo.IsEnabled = Model.CanRedo;
    }

    private void UpdateModeButtons()
    {
        _designButton.IsChecked = IsDesignMode;
        _textButton.IsChecked = !IsDesignMode;
    }

    public void ShowDesign()
    {
        IsDesignMode = true;
        Model.IsLive = true;
        Model.Refresh();
        _textHost.Visibility = Visibility.Collapsed;
        _design.Visibility = Visibility.Visible;
        _editor?.ClosePopups();
        UpdateModeButtons();
        FocusActive();
    }

    public void ShowText()
    {
        IsDesignMode = false;
        Model.IsLive = false;
        if (_editor == null)
        {
            _editor = new CodeEditor(_doc, _vm);
            _textHost.Child = _editor;
        }
        _design.Visibility = Visibility.Collapsed;
        _textHost.Visibility = Visibility.Visible;
        UpdateModeButtons();
        FocusActive();
    }

    private void FocusStage() => Stage.Focus();

    public void FocusActive()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsDesignMode) Stage.Focus();
            else if (_editor != null)
            {
                _editor.Focus();
                _editor.TextArea.Focus();
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>Ir para uma linha (ex.: um erro em Problemas) abre a aba Texto.</summary>
    public void GoTo(int line, int column, int? offset)
    {
        ShowText();
        var editor = _editor!;
        Dispatcher.BeginInvoke(() => editor.GoTo(line, column, offset), DispatcherPriority.Loaded);
    }

    public void ClosePopups() => _editor?.ClosePopups();

    public void Detach()
    {
        Model.Changed -= UpdateState;
        Model.Renamed -= OnRenamed;
        _doc.Document.UndoStack.PropertyChanged -= OnUndoStackChanged;
        Stage.EditTextRequested -= Properties.FocusText;
        Palette.PieceAdded -= FocusStage;
        Stage.Detach();
        Properties.Detach();
        Model.Dispose();
        _editor?.Detach();
    }
}
