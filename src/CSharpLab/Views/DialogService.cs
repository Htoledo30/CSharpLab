using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSharpLab.Core.Projects;
using CSharpLab.ViewModels;
using Microsoft.Win32;

namespace CSharpLab.Views;

public sealed class DialogService : IDialogService
{
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public SaveChoice AskSaveChanges(IReadOnlyList<string> fileNames)
    {
        var dialog = new DialogWindow("Salvar alterações?");
        var panel = new StackPanel();
        if (fileNames.Count == 1)
        {
            panel.Children.Add(DialogWindow.Paragraph($"\"{fileNames[0]}\" tem alterações não salvas."));
        }
        else
        {
            panel.Children.Add(DialogWindow.Paragraph("Estes arquivos têm alterações não salvas:"));
            foreach (var name in fileNames.Take(8))
                panel.Children.Add(new TextBlock { Text = "•  " + name, Margin = new Thickness(6, 4, 0, 0), Foreground = Res("TextSecondary") });
            if (fileNames.Count > 8)
                panel.Children.Add(new TextBlock { Text = $"… e mais {fileNames.Count - 8}", Margin = new Thickness(6, 4, 0, 0), Foreground = Res("TextMuted") });
        }
        dialog.Body = panel;
        dialog.AddButton(fileNames.Count == 1 ? "Salvar" : "Salvar tudo", SaveChoice.Save, "PrimaryButton", isDefault: true);
        dialog.AddButton("Descartar", SaveChoice.Discard);
        dialog.AddButton("Cancelar", SaveChoice.Cancel, isCancel: true);
        return dialog.ShowDialog() == true && dialog.Result is SaveChoice c ? c : SaveChoice.Cancel;
    }

    public bool Confirm(string title, string message, string confirmText, bool danger = false)
    {
        var dialog = new DialogWindow(title) { Body = DialogWindow.Paragraph(message) };
        dialog.AddButton(confirmText, true, danger ? "DangerButton" : "PrimaryButton", isDefault: true);
        dialog.AddButton("Cancelar", false, isCancel: true);
        return dialog.ShowDialog() == true && dialog.Result is true;
    }

    public void ShowError(string title, string message, string? details = null)
    {
        var dialog = new DialogWindow(title);
        var panel = new StackPanel();
        panel.Children.Add(DialogWindow.Paragraph(message));
        if (!string.IsNullOrWhiteSpace(details))
        {
            var box = new TextBox
            {
                Text = details,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 220,
                FontFamily = (FontFamily)Application.Current.FindResource("CodeFont"),
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            var toggle = new Button { Content = "Detalhes", Style = (Style)Application.Current.FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 8, 0, 0) };
            toggle.Click += (_, _) =>
            {
                box.Visibility = box.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                toggle.Content = box.Visibility == Visibility.Visible ? "Ocultar detalhes" : "Detalhes";
            };
            panel.Children.Add(toggle);
            panel.Children.Add(box);
        }
        dialog.Body = panel;
        dialog.AddButton("OK", null, "PrimaryButton", isDefault: true);
        dialog.ShowDialog();
    }

    public ExternalChangeChoice AskExternalChange(string fileName)
    {
        var dialog = new DialogWindow("Arquivo alterado fora do editor")
        {
            Body = DialogWindow.Paragraph($"\"{fileName}\" foi alterado por outro programa, e você tem alterações não salvas nele. Qual versão manter?"),
        };
        dialog.AddButton("Manter a minha", ExternalChangeChoice.KeepMine, "PrimaryButton", isDefault: true);
        dialog.AddButton("Recarregar do disco", ExternalChangeChoice.Reload);
        return dialog.ShowDialog() == true && dialog.Result is ExternalChangeChoice c ? c : ExternalChangeChoice.KeepMine;
    }

    public bool ShowSdkMissing(string message, string? details)
    {
        var dialog = new DialogWindow(message);
        var panel = new StackPanel();
        panel.Children.Add(DialogWindow.Paragraph("O editor funciona sem ele, mas para compilar e executar é preciso o SDK .NET 10 (não apenas o runtime)."));
        var link = new Button { Content = "Abrir página oficial de download", Style = (Style)Application.Current.FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 10, 0, 0) };
        link.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(SdkLocator.DownloadUrl) { UseShellExecute = true }); } catch { }
        };
        panel.Children.Add(link);
        if (!string.IsNullOrWhiteSpace(details))
            panel.Children.Add(new TextBlock { Text = details, Foreground = Res("TextMuted"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        dialog.Body = panel;
        dialog.AddButton("Verificar novamente", true, "PrimaryButton", isDefault: true);
        dialog.AddButton("Fechar", false, isCancel: true);
        return dialog.ShowDialog() == true && dialog.Result is true;
    }

    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (initialDirectory != null && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initialDirectory)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (initialDirectory != null && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string suggestedName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar como",
            FileName = suggestedName,
            Filter = "Arquivo C# (*.cs)|*.cs|Todos os arquivos (*.*)|*.*",
            DefaultExt = ".cs",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (initialDirectory != null && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public NewProjectRequest? AskNewProject(string title, string? explanation, string defaultName, string defaultLocation, bool offerGame = false)
    {
        var dialog = new NewProjectDialog(title, explanation, defaultName, defaultLocation, this, offerGame);
        return dialog.ShowDialog() == true ? dialog.Request : null;
    }

    public void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Não foi possível abrir o navegador", url, ex.Message); }
    }

    public string? AskText(string title, string message, string initial, Func<string, string?>? validate = null)
    {
        var dialog = new TextPromptDialog(title, message, initial, validate);
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public ProjectFile? SelectProject(IReadOnlyList<ProjectFile> projects, string folder)
    {
        var dialog = new DialogWindow("Qual projeto executar?");
        var panel = new StackPanel();
        panel.Children.Add(DialogWindow.Paragraph("Esta pasta tem mais de um projeto. A escolha fica salva para esta pasta.", muted: true));
        var radios = new List<RadioButton>();
        foreach (var p in projects)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = Path.GetRelativePath(folder, p.Path), Foreground = Res("TextMuted"), FontSize = 12 });
            var radio = new RadioButton { Content = content, Tag = p, IsChecked = radios.Count == 0 };
            radios.Add(radio);
            panel.Children.Add(radio);
        }
        radios[0].Margin = new Thickness(0, 12, 0, 3);
        dialog.Body = new ScrollViewer { Content = panel, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.AddButton("Executar este", true, "PrimaryButton", isDefault: true);
        dialog.AddButton("Cancelar", false, isCancel: true);
        if (dialog.ShowDialog() != true || dialog.Result is not true) return null;
        return radios.FirstOrDefault(r => r.IsChecked == true)?.Tag as ProjectFile;
    }

    private static Window? Owner() => Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
}

/// <summary>Pede nome e pasta de destino e, quando oferecido, o tipo: programa console ou jogo com botões.</summary>
public sealed class NewProjectDialog : DialogWindow
{
    private const string DefaultGameName = "MeuJogo";
    private readonly TextBox _name;
    private readonly RadioButton? _game;
    private readonly TextBox _location;
    private readonly TextBlock _preview;
    private readonly TextBlock _error;

    public NewProjectDialog(string title, string? explanation, string defaultName, string defaultLocation, DialogService dialogs, bool offerGame = false) : base(title)
    {
        Width = 520;
        var panel = new StackPanel();
        if (explanation != null)
            panel.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondary"), Margin = new Thickness(0, 0, 0, 12) });

        _name = new TextBox { Text = defaultName };
        if (offerGame)
        {
            panel.Children.Add(Label("Tipo"));
            var console = KindOption("Programa console", "Texto no terminal: Console.WriteLine, ReadLine, jogos de terminal.", isChecked: true);
            _game = KindOption("Jogo com botões", "Janela com botões, barras de vida e telas que você desenha arrastando.", isChecked: false);
            panel.Children.Add(console);
            panel.Children.Add(_game);
            // O nome padrão acompanha o tipo, enquanto a pessoa não escolher outro.
            _game.Checked += (_, _) => { if (_name.Text.Trim() == defaultName) _name.Text = DefaultGameName; };
            console.Checked += (_, _) => { if (_name.Text.Trim() == DefaultGameName) _name.Text = defaultName; };
            panel.Children.Add(Label("Nome", top: 12));
        }
        else
        {
            panel.Children.Add(Label("Nome"));
        }
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome do projeto");
        panel.Children.Add(_name);

        panel.Children.Add(Label("Local", top: 12));
        var row = new DockPanel();
        var browse = new Button { Content = "Procurar…", Style = (Style)FindResource("DialogButton"), Height = 30 };
        DockPanel.SetDock(browse, Dock.Right);
        _location = new TextBox { Text = defaultLocation };
        System.Windows.Automation.AutomationProperties.SetName(_location, "Pasta de destino");
        row.Children.Add(browse);
        row.Children.Add(_location);
        panel.Children.Add(row);
        browse.Click += (_, _) =>
        {
            var folder = dialogs.PickFolder("Escolher local do projeto", _location.Text);
            if (folder != null) _location.Text = folder;
        };

        _preview = new TextBlock { Foreground = (Brush)FindResource("TextMuted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        _error = new TextBlock { Foreground = (Brush)FindResource("ErrorBrush"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        panel.Children.Add(_preview);
        panel.Children.Add(_error);
        Body = panel;

        _name.TextChanged += (_, _) => UpdatePreview();
        _location.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        AddButton("Criar", true, "PrimaryButton", isDefault: true);
        AddButton("Cancelar", false, isCancel: true);
        InitialFocus = _name;
        Loaded += (_, _) => _name.SelectAll();
    }

    public NewProjectRequest? Request { get; private set; }

    private static RadioButton KindOption(string title, string detail, bool isChecked)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = (Brush)Application.Current.FindResource("TextMuted"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var radio = new RadioButton { Content = content, IsChecked = isChecked, GroupName = "ProjectKind" };
        System.Windows.Automation.AutomationProperties.SetName(radio, title);
        return radio;
    }

    private static TextBlock Label(string text, double top = 0) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        Margin = new Thickness(0, top, 0, 5),
    };

    private void UpdatePreview()
    {
        _error.Visibility = Visibility.Collapsed;
        var name = _name.Text.Trim();
        try
        {
            _preview.Text = name.Length > 0 && _location.Text.Trim().Length > 0
                ? "Será criado em: " + Path.Combine(_location.Text.Trim(), name)
                : "";
        }
        catch
        {
            _preview.Text = "";
        }
    }

    protected override bool OnButton(object? result)
    {
        if (result is not true) return true;
        var name = _name.Text.Trim();
        var location = _location.Text.Trim();
        var error = ProjectCreator.ValidateProjectName(name);
        if (error == null && (location.Length == 0 || !Directory.Exists(location)))
            error = "A pasta de destino não existe.";
        if (error == null)
        {
            var target = Path.Combine(location, name);
            if (File.Exists(target)) error = $"Já existe um arquivo chamado \"{name}\" nesse local.";
            else if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                error = $"A pasta \"{name}\" já existe e não está vazia.";
        }
        if (error != null)
        {
            _error.Text = error;
            _error.Visibility = Visibility.Visible;
            return false;
        }
        Request = new NewProjectRequest(name, location, IsGame: _game?.IsChecked == true);
        return true;
    }
}

/// <summary>Pede um texto curto, com validação na hora (ex.: o novo nome ao renomear).</summary>
public sealed class TextPromptDialog : DialogWindow
{
    private readonly TextBox _box;
    private readonly TextBlock _error;
    private readonly Func<string, string?>? _validate;

    public TextPromptDialog(string title, string message, string initial, Func<string, string?>? validate) : base(title)
    {
        _validate = validate;
        var panel = new StackPanel();
        panel.Children.Add(Paragraph(message, muted: true));
        _box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0), FontFamily = (FontFamily)FindResource("CodeFont") };
        System.Windows.Automation.AutomationProperties.SetName(_box, message);
        _error = new TextBlock { Foreground = (Brush)FindResource("ErrorBrush"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        panel.Children.Add(_box);
        panel.Children.Add(_error);
        Body = panel;
        _box.TextChanged += (_, _) => _error.Visibility = Visibility.Collapsed;
        AddButton("OK", true, "PrimaryButton", isDefault: true);
        AddButton("Cancelar", false, isCancel: true);
        InitialFocus = _box;
        Loaded += (_, _) => _box.SelectAll();
    }

    public string Value => _box.Text.Trim();

    protected override bool OnButton(object? result)
    {
        if (result is not true) return true;
        var error = Value.Length == 0 ? "Digite um nome." : _validate?.Invoke(Value);
        if (error == null) return true;
        _error.Text = error;
        _error.Visibility = Visibility.Visible;
        return false;
    }
}
