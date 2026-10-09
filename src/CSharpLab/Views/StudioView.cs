using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpLab.Core.Projects;
using CSharpLab.Editor;
using CSharpLab.Screens;
using CSharpLab.ViewModels;

namespace CSharpLab.Views;

/// <summary>
/// O Estúdio do jogo: um lugar só para o jogo, sem abas. À esquerda, todas as cenas e as camadas da cena
/// aberta; no meio, a tela para arrastar as peças; à direita, o código de verdade (o mesmo Program.cs),
/// já no game.Scene da cena. Trocar de cena troca as três coisas juntas.
/// </summary>
public sealed class StudioView : Grid
{
    private const string ScreenGlyph = "";
    private const string CodeGlyph = "";

    private readonly Dictionary<DocumentViewModel, ScreenEditorView> _screens = [];
    private readonly Dictionary<DocumentViewModel, (CodeEditor Editor, SceneRegionRenderer Region)> _editors = [];
    private readonly StackPanel _sceneList = new() { Margin = new Thickness(6, 0, 6, 6) };
    private readonly LayersPanel _layers = new();
    private readonly Grid _center = new();
    private readonly Border _noScreen = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _noScreenTitle = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
    private readonly TextBlock _noScreenText = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private readonly Button _noScreenAction;
    private readonly Grid _codeHost = new();
    private readonly TextBlock _codeTitle = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _missingScene = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 8, 10, 8) };
    private readonly TextBlock _missingText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _play = new();
    private readonly SceneMapView _map = new() { Visibility = Visibility.Collapsed };
    private bool _mapMode;
    private readonly TextBlock _gameName = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(10, 1, 0, 0) };
    private readonly ColumnDefinition _codeColumn = new() { Width = new GridLength(0.36, GridUnitType.Star), MinWidth = 260 };
    private readonly DispatcherTimer _caretTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _refreshTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
    private MainViewModel? _vm;
    private string? _scene;
    private ScreenEditorView? _screen;
    private CodeEditor? _code;
    private bool _codeFocusedLast;
    private bool _syncing;
    private ScreenDesignerModel? _watched;
    private bool _selectingFromCode;
    private string? _codeNote;

    public StudioView()
    {
        SetResourceReference(BackgroundProperty, "BgEditor");
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(Header());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.64, GridUnitType.Star), MinWidth = 420 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        body.ColumnDefinitions.Add(_codeColumn);
        SetRow(body, 1);
        Children.Add(body);

        body.Children.Add(Sidebar());

        _noScreenAction = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        _noScreenAction.SetResourceReference(StyleProperty, "PrimaryButton");
        _noScreenAction.Margin = new Thickness(0);
        _noScreen.Child = NoScreenCard();
        _noScreen.SetResourceReference(Border.BackgroundProperty, "BgBase");
        _center.Children.Add(_noScreen);
        _center.Children.Add(_map);
        _map.SceneChosen += scene =>
        {
            ShowScene(scene);
            FocusActive();
        };
        SetColumn(_center, 1);
        body.Children.Add(_center);

        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        splitter.SetResourceReference(StyleProperty, "Splitter");
        splitter.SetResourceReference(BackgroundProperty, "BgSidebar");
        SetColumn(splitter, 2);
        body.Children.Add(splitter);

        var code = CodePane();
        SetColumn(code, 3);
        body.Children.Add(code);

        _layers.PieceChosen += () => _screen?.Stage.Focus();
        _caretTimer.Tick += (_, _) =>
        {
            _caretTimer.Stop();
            FollowCaret();
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshScenes();
        };
    }

    public bool IsOpen => _vm?.IsStudioOpen == true;

    /// <summary>A busca (Ctrl+F) do código do Estúdio.</summary>
    public FindReplaceBar FindBar { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 0, 22, 0),
    };

    /// <summary>O editor de código, quando foi ele o último a receber o teclado (para Desfazer, Buscar etc. do menu).</summary>
    public CodeEditor? ActiveEditor => _codeFocusedLast ? _code : null;

    /// <summary>A tela, quando foi ela a última a receber o teclado.</summary>
    public ScreenEditorView? ActiveScreen => !_codeFocusedLast && _screen is { IsDesignMode: true } screen ? screen : null;

    /// <summary>O editor de texto que o menu deve usar: o código, ou o Arquivo da tela.</summary>
    public CodeEditor? MenuEditor => _codeFocusedLast ? _code : _screen?.ActiveTextEditor;

    public string? CurrentScene => _scene;

    // Para os testes olharem o que está à vista.
    internal LayersPanel Layers => _layers;
    internal ScreenEditorView? Screen => _screen;
    internal CodeEditor? Code => _code;
    internal bool ShowsNoScreen => _noScreen.Visibility == Visibility.Visible;
    internal bool ShowsMissingScene => _missingScene.Visibility == Visibility.Visible;
    internal bool ShowsMap => _map.Visibility == Visibility.Visible;
    internal SceneMapView Map => _map;
    internal int MarkCount => _code != null && _editors.TryGetValue(_code.Doc, out var marked) ? marked.Region.Marks.Count : 0;
    internal (int Start, int End)? SceneSpan => _code != null && _editors.TryGetValue(_code.Doc, out var entry) ? entry.Region.Span : null;
    internal IEnumerable<string> SceneRows => _sceneList.Children.OfType<Border>().Select(row =>
        string.Join(" ", ((DockPanel)row.Child).Children.OfType<TextBlock>().Select(t => t.Text)));

    // ------------------------------------------------------------------ montagem

    private Border Header()
    {
        var header = new DockPanel { LastChildFill = true };
        var border = new Border { Child = header, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(14, 0, 8, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "BgSidebar");

        var back = new Button { ToolTip = "Fechar o Estúdio e voltar para o editor com abas", VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        back.Content = WithIcon("", "Voltar ao editor");
        back.SetResourceReference(StyleProperty, "TextButton");
        back.Click += (_, _) => _vm?.CloseStudio();
        DockPanel.SetDock(back, Dock.Right);
        header.Children.Add(back);

        _play.Content = WithIcon("\uE768", "Jogar daqui");
        _play.ToolTip = "Abre o jogo já nesta cena, para testar rápido. As variáveis começam com o valor do começo do código.";
        _play.VerticalAlignment = VerticalAlignment.Center;
        _play.Margin = new Thickness(0, 0, 10, 0);
        _play.Cursor = Cursors.Hand;
        _play.SetResourceReference(StyleProperty, "TextButton");
        _play.SetResourceReference(Control.ForegroundProperty, "Accent");
        _play.Click += (_, _) =>
        {
            if (_vm != null && _scene != null) _vm.PlayFromScene(_scene);
        };
        DockPanel.SetDock(_play, Dock.Right);
        header.Children.Add(_play);

        var export = new Button
        {
            Content = WithIcon("\uE7B8", "Exportar"),
            ToolTip = "Gera o .exe do jogo e um .zip para mandar aos amigos (roda em qualquer Windows, sem instalar nada)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
        };
        export.SetResourceReference(StyleProperty, "TextButton");
        export.Click += (_, _) => _vm?.ExportGameCommand.Execute(null);
        DockPanel.SetDock(export, Dock.Right);
        header.Children.Add(export);

        var icon = new TextBlock { Text = ScreenGlyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var title = new TextBlock { Text = "Estúdio do jogo", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _gameName.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(icon);
        left.Children.Add(title);
        left.Children.Add(_gameName);
        header.Children.Add(left);
        return border;
    }

    private static StackPanel WithIcon(string glyph, string text)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    private Border Sidebar()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 360 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(14, 10, 8, 6) };
        var add = new Button { Content = "", ToolTip = "Nova cena: cria a tela (e, se quiser, o começo do código dela)", Width = 24, Height = 22, FontSize = 11, Focusable = false };
        add.SetResourceReference(StyleProperty, "IconButton");
        add.Click += (_, _) => _vm?.NewScreenCommand.Execute(null);
        DockPanel.SetDock(add, Dock.Right);
        header.Children.Add(add);
        var title = new TextBlock { Text = "CENAS", FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        header.Children.Add(title);
        grid.Children.Add(header);

        var scenes = new ScrollViewer { Content = _sceneList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        SetRow(scenes, 1);
        grid.Children.Add(scenes);

        var line = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        SetRow(line, 2);
        grid.Children.Add(line);

        SetRow(_layers, 3);
        grid.Children.Add(_layers);

        var border = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 1, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "BgSidebar");
        return border;
    }

    private FrameworkElement NoScreenCard()
    {
        var icon = new TextBlock { Text = CodeGlyph, FontSize = 22 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        _noScreenTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _noScreenText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _noScreenAction.Click += (_, _) =>
        {
            if (_vm == null) return;
            if (_scene == null) _vm.NewScreenCommand.Execute(null);
            else if (_vm.GameDirectory is { } dir) _vm.OpenSceneScreen(dir, _scene);
        };
        var stack = new StackPanel { MaxWidth = 440 };
        stack.Children.Add(icon);
        stack.Children.Add(_noScreenTitle);
        stack.Children.Add(_noScreenText);
        stack.Children.Add(_noScreenAction);
        var card = new Border
        {
            Child = stack,
            Padding = new Thickness(24, 20, 24, 20),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrong");
        return card;
    }

    private Grid CodePane()
    {
        var pane = new Grid();
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock { Text = CodeGlyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        _codeTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(_codeTitle);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 8, 0) };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        headerBorder.SetResourceReference(Border.BackgroundProperty, "BgSidebar");
        pane.Children.Add(headerBorder);

        // A cena ainda não existe no código: oferece escrever o começo dela.
        var write = new Button { Content = "Escrever a cena", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        write.SetResourceReference(StyleProperty, "DialogButton");
        write.Click += (_, _) =>
        {
            if (_vm?.GameDirectory is { } dir && _scene != null) _vm.GoToSceneCode(dir, _scene);
        };
        var missing = new DockPanel();
        DockPanel.SetDock(write, Dock.Right);
        missing.Children.Add(write);
        _missingText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        missing.Children.Add(_missingText);
        _missingScene.Child = missing;
        _missingScene.SetResourceReference(Border.BackgroundProperty, "BgElevated");
        SetRow(_missingScene, 1);
        pane.Children.Add(_missingScene);

        SetRow(_codeHost, 2);
        pane.Children.Add(_codeHost);
        Panel.SetZIndex(FindBar, 10);
        SetRow(FindBar, 2);
        pane.Children.Add(FindBar);
        return pane;
    }

    // ------------------------------------------------------------------ ligação com o resto do app

    public void Bind(MainViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.GoToRequested += OnGoToRequested;
        vm.ScreenDesignRequested += doc =>
        {
            if (IsOpen && doc.FilePath != null) ShowScene(Path.GetFileNameWithoutExtension(doc.FilePath));
        };
        vm.Documents.CollectionChanged += OnDocumentsChanged;
        vm.FocusEditorRequested += () =>
        {
            if (IsOpen) FocusActive();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null || !IsOpen || _syncing) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ActiveDocument):
                Follow(_vm.ActiveDocument);
                break;
            case nameof(MainViewModel.HasGame):
                if (!_vm.HasGame) _vm.CloseStudio();
                break;
        }
    }

    /// <summary>Outro lugar do app abriu um arquivo: se é do jogo, aparece aqui; se não é, o Estúdio fecha para ele aparecer.</summary>
    private void Follow(DocumentViewModel? doc)
    {
        if (_vm == null || doc == null) return;
        if (!_vm.BelongsToGame(doc))
        {
            _vm.CloseStudio();
            return;
        }
        if (doc.IsScreen)
        {
            var scene = Path.GetFileNameWithoutExtension(doc.FilePath!);
            if (!string.Equals(scene, _scene, StringComparison.OrdinalIgnoreCase)) ShowScene(scene);
        }
        else if (_code?.Doc != doc)
        {
            ShowCode(doc);
        }
    }

    private void OnGoToRequested(DocumentViewModel doc, int line, int column, int? offset)
    {
        if (_vm == null || !IsOpen) return;
        if (!_vm.BelongsToGame(doc))
        {
            _vm.CloseStudio();
            return;
        }
        if (doc.IsScreen)
        {
            ShowScene(Path.GetFileNameWithoutExtension(doc.FilePath!));
            _screen?.GoTo(line, column, offset);
            return;
        }
        var editor = ShowCode(doc);
        // A cena pode ter acabado de ser escrita ("Escrever a cena"): o aviso de que ela falta sai.
        if (_scene != null && _vm.GameDirectory is { } dir && _vm.FindSceneCode(dir, _scene) != null)
            _missingScene.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(() =>
        {
            editor.GoTo(line, column, offset);
            FollowCaret();
        }, DispatcherPriority.Loaded);
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm == null) return;
        // Uma aba fechada some daqui também (o Estúdio nunca mostra um arquivo que o app já não acompanha).
        foreach (var doc in _screens.Keys.Where(d => !_vm.Documents.Contains(d)).ToList())
        {
            var view = _screens[doc];
            view.Detach();
            _center.Children.Remove(view);
            _screens.Remove(doc);
            if (_screen == view) _screen = null;
        }
        foreach (var doc in _editors.Keys.Where(d => !_vm.Documents.Contains(d)).ToList())
        {
            var (editor, _) = _editors[doc];
            editor.Detach();
            _codeHost.Children.Remove(editor);
            _editors.Remove(doc);
            if (_code == editor) _code = null;
        }
        if (IsOpen) _refreshTimer.Start();
    }

    // ------------------------------------------------------------------ abrir e fechar

    /// <summary>O Estúdio apareceu: mostra a cena pedida (ou a última, ou a do começo do jogo).</summary>
    public void Open(string? scene)
    {
        if (_vm?.GameDirectory is not { } dir)
        {
            _vm?.CloseStudio();
            return;
        }
        _gameName.Text = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        var scenes = _vm.GameScenes();
        scene ??= _vm.StudioScene;
        if (scene == null || !scenes.Any(s => string.Equals(s.Name, scene, StringComparison.OrdinalIgnoreCase)))
            scene = scenes.FirstOrDefault(s => s.IsStart)?.Name ?? scenes.FirstOrDefault()?.Name;
        _scene = null;
        ShowScene(scene, scenes);
        FocusActive();
    }

    /// <summary>O Estúdio vai fechar: o editor com abas continua no código que estava aqui.</summary>
    public void Leave()
    {
        _caretTimer.Stop();
        _refreshTimer.Stop();
        FindBar.Close();
        _code?.ClosePopups();
        _screen?.ClosePopups();
        if (_vm != null && _code != null && _vm.Documents.Contains(_code.Doc))
        {
            _syncing = true;
            try { _vm.ActiveDocument = _code.Doc; }
            finally { _syncing = false; }
        }
    }

    public void FocusActive()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_codeFocusedLast && _code != null)
            {
                _code.Focus();
                _code.TextArea.Focus();
            }
            else _screen?.FocusActive();
        }, DispatcherPriority.Input);
    }

    // ------------------------------------------------------------------ cenas

    private void RefreshScenes()
    {
        if (_mapMode && _vm?.GameDirectory is { } dir) _map.Show(_vm, dir, _scene);
        BuildSceneList(_vm?.GameScenes() ?? []);
        MarkPiece(_watched?.SelectedName, scroll: false);
    }

    private void BuildSceneList(IReadOnlyList<GameSceneInfo> scenes)
    {
        _sceneList.Children.Clear();
        _sceneList.Children.Add(MapRow());
        if (scenes.Count == 0)
        {
            var empty = new TextBlock { Text = "Nenhuma cena ainda. Crie uma no + acima.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 0) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            _sceneList.Children.Add(empty);
            return;
        }
        foreach (var scene in scenes) _sceneList.Children.Add(SceneRow(scene));
    }

    /// <summary>"Mapa do jogo": todas as cenas e as setas de quem leva para onde.</summary>
    private Border MapRow()
    {
        var icon = new TextBlock { Text = "\uE707", FontSize = 12, Width = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, _mapMode ? "Accent" : "TextMuted");
        var text = new TextBlock { Text = "Mapa do jogo", VerticalAlignment = VerticalAlignment.Center, FontWeight = _mapMode ? FontWeights.SemiBold : FontWeights.Normal };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(text);
        var row = new Border
        {
            Child = line,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = "Todas as cenas numa tela só, com setas de para onde cada uma leva (os game.GoTo do código)",
        };
        if (_mapMode) row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        else
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "BgHover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }
        row.MouseLeftButtonUp += (_, e) =>
        {
            ShowMap();
            e.Handled = true;
        };
        return row;
    }

    /// <summary>O mapa no lugar da tela (o código continua à direita).</summary>
    public void ShowMap()
    {
        if (_vm?.GameDirectory is not { } dir) return;
        _mapMode = true;
        foreach (var view in _screens.Values) view.Visibility = Visibility.Collapsed;
        _noScreen.Visibility = Visibility.Collapsed;
        _layers.Model = null;
        _layers.EmptyMessage = "No mapa: clique numa cena para ver as camadas dela.";
        Watch(null);
        _map.Visibility = Visibility.Visible;
        _map.Show(_vm, dir, _scene);
        BuildSceneList(_vm.GameScenes());
    }

    private Border SceneRow(GameSceneInfo scene)
    {
        bool current = !_mapMode && string.Equals(scene.Name, _scene, StringComparison.OrdinalIgnoreCase);
        var icon = new TextBlock { Text = scene.HasScreen ? ScreenGlyph : CodeGlyph, FontSize = 12, Width = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, current ? "Accent" : "TextMuted");
        var name = new TextBlock { Text = scene.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        if (current) name.FontWeight = FontWeights.SemiBold;
        var note = scene.IsStart ? "começa aqui" : !scene.InCode ? "falta no código" : !scene.HasScreen ? "só código" : null;

        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        if (note != null)
        {
            var hint = new TextBlock { Text = note, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            DockPanel.SetDock(hint, Dock.Right);
            line.Children.Add(hint);
        }
        line.Children.Add(name);

        var row = new Border
        {
            Child = line,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = scene.HasScreen ? $"Cena \"{scene.Name}\": a tela no meio, o código à direita" : $"Cena \"{scene.Name}\": feita só com código",
        };
        System.Windows.Automation.AutomationProperties.SetName(row, "Cena " + scene.Name);
        if (current) row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        else
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "BgHover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }
        row.MouseLeftButtonUp += (_, e) =>
        {
            ShowScene(scene.Name);
            FocusActive();
            e.Handled = true;
        };
        return row;
    }

    /// <summary>Troca de cena: a tela no meio, as camadas dela e o código já no game.Scene dela.</summary>
    public void ShowScene(string? scene, IReadOnlyList<GameSceneInfo>? scenes = null)
    {
        if (_vm?.GameDirectory is not { } dir) return;
        _caretTimer.Stop();
        _mapMode = false;
        _map.Visibility = Visibility.Collapsed;
        _scene = scene;
        if (scene != null) _vm.StudioScene = scene;
        var list = scenes ?? _vm.GameScenes();
        _play.IsEnabled = list.Any(s => s.InCode && string.Equals(s.Name, scene, StringComparison.OrdinalIgnoreCase));
        BuildSceneList(list);
        ShowScreen(dir, scene);
        ShowSceneCode(dir, scene);
    }

    private void ShowScreen(string dir, string? scene)
    {
        ScreenEditorView? view = null;
        if (scene != null && _vm!.HasScreen(dir, scene))
        {
            var doc = OpenQuietly(GameScreens.PathOf(dir, scene));
            if (doc != null && !_screens.TryGetValue(doc, out view))
            {
                view = new ScreenEditorView(doc, _vm) { ShowCodeButton = false };
                view.IsKeyboardFocusWithinChanged += (_, e) =>
                {
                    if (e.NewValue is true) OnPartFocused(code: false, view.Model.Doc);
                };
                _screens[doc] = view;
                _center.Children.Add(view);
            }
        }
        _screen = view;
        foreach (var other in _screens.Values)
        {
            if (other != view) other.ClosePopups();
            other.Visibility = other == view ? Visibility.Visible : Visibility.Collapsed;
        }
        _layers.EmptyMessage = "Esta cena ainda não tem tela desenhada.";
        _layers.Model = view?.Model;
        Watch(view?.Model);

        _noScreen.Visibility = view == null ? Visibility.Visible : Visibility.Collapsed;
        if (view == null)
        {
            if (scene == null)
            {
                _noScreenTitle.Text = "O jogo ainda não tem cenas";
                _noScreenText.Text = "Cada cena é um lugar do jogo (a vila, a floresta, a luta). Crie a primeira: ela ganha uma tela para desenhar e o começo do código.";
                _noScreenAction.Content = "Criar a primeira cena";
            }
            else
            {
                _noScreenTitle.Text = $"A cena \"{scene}\" é feita só com código";
                _noScreenText.Text = "Ela ainda não tem tela desenhada: o que aparece vem do código à direita. Com uma tela, você arrasta os textos, botões e barras, e o código liga eles com game.Find(\"Nome\").";
                _noScreenAction.Content = "Desenhar a tela desta cena";
            }
        }
    }

    private void ShowSceneCode(string dir, string? scene)
    {
        var found = scene != null ? _vm!.FindSceneCode(dir, scene) : null;
        var file = found?.File ?? Path.Combine(dir, "Program.cs");
        var doc = _code != null && string.Equals(_code.Doc.FilePath, file, StringComparison.OrdinalIgnoreCase) ? _code.Doc : OpenQuietly(file);
        if (doc == null)
        {
            _missingScene.Visibility = Visibility.Collapsed;
            return;
        }
        var editor = ShowCode(doc);
        _missingScene.Visibility = scene != null && found == null ? Visibility.Visible : Visibility.Collapsed;
        _missingText.Text = $"A cena \"{scene}\" ainda não está no código.";
        if (found != null)
        {
            // A cena no alto do código, com o cursor no começo dela (sem tirar o teclado de onde está).
            Dispatcher.BeginInvoke(() =>
            {
                if (_code != editor) return;
                int offset = Math.Min(found.Offset, editor.Document.TextLength);
                editor.TextArea.Caret.Offset = offset;
                editor.TextArea.ClearSelection();
                _caretTimer.Stop();   // quem moveu o cursor foi a troca de cena, não a pessoa
                var line = editor.Document.GetLineByOffset(offset).LineNumber;
                var top = editor.TextArea.TextView.GetVisualTopByDocumentLine(Math.Max(1, line - 2));
                editor.ScrollToVerticalOffset(top);
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Mostra um arquivo de código à direita (cada arquivo guarda o próprio editor, com cursor e rolagem).</summary>
    private CodeEditor ShowCode(DocumentViewModel doc)
    {
        if (!_editors.TryGetValue(doc, out var entry))
        {
            var editor = new CodeEditor(doc, _vm!);
            var region = new SceneRegionRenderer(editor);
            editor.TextArea.TextView.BackgroundRenderers.Insert(0, region);
            editor.TextArea.Caret.PositionChanged += (_, _) =>
            {
                if (IsOpen && _code == editor) _caretTimer.Start();
            };
            editor.IsKeyboardFocusWithinChanged += (_, e) =>
            {
                if (e.NewValue is true) OnPartFocused(code: true, doc);
            };
            doc.TextChanged += _ =>
            {
                if (IsOpen) _refreshTimer.Start();
            };
            entry = (editor, region);
            _editors[doc] = entry;
            _codeHost.Children.Add(editor);
        }
        foreach (var (other, _) in _editors.Values)
        {
            if (other != entry.Editor) other.ClosePopups();
            other.Visibility = other == entry.Editor ? Visibility.Visible : Visibility.Collapsed;
        }
        _code = entry.Editor;
        entry.Region.SetScene(_scene);
        UpdateCodeTitle();
        if (FindBar.IsOpen) FindBar.Attach(_code);
        return _code;
    }

    /// <summary>Abre o arquivo no app sem mexer na aba ativa (o Estúdio mostra ele aqui).</summary>
    private DocumentViewModel? OpenQuietly(string path)
    {
        if (_vm == null) return null;
        if (_vm.FindDocument(path) is { } open) return open;
        if (!File.Exists(path)) return null;
        _syncing = true;
        try { return _vm.OpenFile(path, activate: false); }
        finally { _syncing = false; }
    }

    /// <summary>A parte que recebeu o teclado vira o arquivo ativo do app (Salvar, Desfazer e o menu valem para ela).</summary>
    private void OnPartFocused(bool code, DocumentViewModel doc)
    {
        _codeFocusedLast = code;
        if (_vm == null || _vm.ActiveDocument == doc || !_vm.Documents.Contains(doc)) return;
        _syncing = true;
        try { _vm.ActiveDocument = doc; }
        finally { _syncing = false; }
        if (code) _code?.ReportCaret();
    }

    /// <summary>O cursor do código entrou em outra cena: a tela do meio vai junto.</summary>
    private void FollowCaret()
    {
        if (_vm?.GameDirectory is not { } dir || _code == null || !_code.Doc.IsCSharp) return;
        string? scene;
        try
        {
            scene = Core.Language.GameAssist.SceneAt(_code.Syntax.Root, _code.CaretOffset)?.Name;
        }
        catch
        {
            return;
        }
        if (scene == null || _mapMode) return;
        if (!string.Equals(scene, _scene, StringComparison.OrdinalIgnoreCase))
        {
            _scene = scene;
            _vm.StudioScene = scene;
            _play.IsEnabled = true;
            BuildSceneList(_vm.GameScenes());
            ShowScreen(dir, scene);
            _missingScene.Visibility = Visibility.Collapsed;
            if (_editors.TryGetValue(_code.Doc, out var entry)) entry.Region.SetScene(scene);
            UpdateCodeTitle();
        }
        SelectPieceAtCaret();
    }

    // ------------------------------------------------------------------ peça ↔ código

    /// <summary>Passa a acompanhar a seleção da tela que está à vista.</summary>
    private void Watch(ScreenDesignerModel? model)
    {
        if (_watched == model) return;
        if (_watched != null) _watched.SelectionChanged -= OnSelectionChanged;
        _watched = model;
        if (_watched != null) _watched.SelectionChanged += OnSelectionChanged;
        MarkPiece(_watched?.SelectedName, scroll: false);
    }

    /// <summary>Peça escolhida na tela (ou nas camadas): o código marca onde ela é usada e rola até lá.</summary>
    private void OnSelectionChanged()
    {
        if (_selectingFromCode) return;
        MarkPiece(_watched?.SelectedName, scroll: true);
    }

    /// <summary>Marca em dourado cada game.Find("Nome") da peça na cena; com scroll, leva o código até o primeiro.</summary>
    private void MarkPiece(string? name, bool scroll)
    {
        if (_code == null || !_editors.TryGetValue(_code.Doc, out var entry)) return;
        IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> spans = [];
        if (name != null && _scene != null && _code.Doc.IsCSharp)
        {
            try { spans = Core.Language.GameAssist.FindPieceReferences(_code.Syntax.Root, _scene, name); }
            catch { spans = []; }
        }
        entry.Region.SetMarks(spans.Select(s => (s.Start, s.Length)).ToList());
        _codeNote = name == null ? null
            : spans.Count == 0 ? $"\"{name}\" ainda não aparece no código desta cena"
            : spans.Count == 1 ? $"\"{name}\": 1 lugar no código"
            : $"\"{name}\": {spans.Count} lugares no código";
        UpdateCodeTitle();
        if (scroll && spans.Count > 0 && !_code.IsKeyboardFocusWithin)
        {
            var editor = _code;
            var at = spans[0].Start;
            Dispatcher.BeginInvoke(() =>
            {
                if (_code != editor || at > editor.Document.TextLength) return;
                var location = editor.Document.GetLocation(at);
                editor.ScrollTo(location.Line, location.Column);
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Cursor do código num game.Find("Nome") (ou dentro do OnClick dele): a peça fica selecionada na tela.</summary>
    private void SelectPieceAtCaret()
    {
        if (_code == null || _watched?.Layout == null || _scene == null) return;
        (string? Scene, string Name)? piece;
        try { piece = Core.Language.GameAssist.PieceAt(_code.Syntax.Root, _code.CaretOffset); }
        catch { return; }
        if (piece is not { } found || !string.Equals(found.Scene, _scene, StringComparison.OrdinalIgnoreCase) || _watched.Layout.Find(found.Name) == null) return;
        if (string.Equals(_watched.SelectedName, found.Name, StringComparison.OrdinalIgnoreCase)) return;
        _selectingFromCode = true;
        try { _watched.Select(found.Name); }
        finally { _selectingFromCode = false; }
        MarkPiece(_watched.SelectedName, scroll: false);
    }

    private void UpdateCodeTitle()
    {
        if (_code == null) return;
        _codeTitle.Inlines.Clear();
        _codeTitle.Inlines.Add(new System.Windows.Documents.Run(_code.Doc.FileName + (_scene != null ? $"  ·  cena {_scene}" : "")));
        if (_codeNote != null)
        {
            var note = new System.Windows.Documents.Run("    " + _codeNote) { FontSize = 11.5 };
            note.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WarningBrush");
            _codeTitle.Inlines.Add(note);
        }
    }
}
