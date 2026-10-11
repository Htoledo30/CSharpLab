using System.Diagnostics;
using System.IO.Compression;
using CSharpLab.Core.Build;
using CSharpLab.Core.Projects;
using CSharpLab.GameEngine;

namespace CSharpLab.Tests;

[CollectionDefinition("Exportação real", DisableParallelization = true)]
public sealed class GameExportIntegrationCollection;

[Collection("Exportação real")]
public sealed class GameExportIntegrationTests
{
    [Fact]
    public async Task Jogo_publicado_e_extraido_abre_com_tela_imagem_e_tema()
    {
        var root = Path.Combine(TestData.Root, "export-real", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "codigo"));
        var created = ProjectCreator.CreateGameProject(Path.Combine(root, "codigo"), "Aventura", """
            var game = new Game(args.FirstOrDefault() ?? "Teste de exportação");
            // A pasta isolada evita que este teste use os saves pessoais de quem está rodando.
            typeof(Game).GetProperty("SaveFolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(game, Path.Combine(AppContext.BaseDirectory, "saves-do-teste"));
            int launches = game.Load("launches", 0) + 1;
            game.Save("launches", launches);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "identidade.txt"), game.SaveId);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "aberturas.txt"), launches.ToString());
            game.Scene("Start", () =>
            {
                game.Find("Title").Text = "Pacote conferido";
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = System.Windows.Application.Current.MainWindow;
                    window.Left = -10000;
                    window.Top = -10000;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        bool text = Descendants(window).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "Pacote conferido");
                        bool image = Descendants(window).OfType<System.Windows.Controls.Image>().Any(i => i.Source != null);
                        // O tema Fantasy usa a fonte de títulos Gabriola.
                        bool theme = Descendants(window).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "Pacote conferido" && t.FontFamily.Source == "Gabriola, Segoe UI");
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "abriu.txt"), $"{text},{image},{theme}");
                        window.Close();
                    };
                    timer.Start();
                }));
            });
            game.Start("Start");

            static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    yield return child;
                    foreach (var item in Descendants(child)) yield return item;
                }
            }
            """);
        var layout = ScreenLayout.CreateDefault("Start");
        var image = Piece.CreateDefault(PieceType.Image, "Picture", 48, 150);
        image.Image = "pixel.png";
        layout.Pieces.Add(image);
        Directory.CreateDirectory(Path.Combine(created.Directory, "Screens"));
        Directory.CreateDirectory(Path.Combine(created.Directory, "Assets"));
        File.WriteAllText(Path.Combine(created.Directory, "Screens", "Start.json"), ScreenFile.Serialize(layout));
        // Uma imagem pequena de teste, sem depender de download ou de arquivos do usuário.
        File.WriteAllBytes(Path.Combine(created.Directory, "Assets", "pixel.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII="));
        File.WriteAllText(Path.Combine(created.Directory, "GameStyle.json"), """{ "format": 1, "theme": "Fantasy" }""");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var exported = await new GameExportService().ExportAsync(ProjectFile.Read(created.ProjectPath), Path.Combine(root, "pacotes"), null, timeout.Token);

        var extracted = Path.Combine(root, "extraido");
        ZipFile.ExtractToDirectory(exported.Zip, extracted);
        var gameFolder = Path.Combine(extracted, "Aventura");
        Assert.Equal(File.ReadAllBytes(Path.Combine(created.Directory, "Screens", "Start.json")), File.ReadAllBytes(Path.Combine(gameFolder, "Screens", "Start.json")));
        Assert.True(File.Exists(Path.Combine(gameFolder, "Assets", "pixel.png")));
        Assert.True(File.Exists(Path.Combine(gameFolder, "GameStyle.json")));
        Assert.False(File.Exists(Path.Combine(gameFolder, "CSharpLab.Game.xml")));

        using var process = Process.Start(new ProcessStartInfo(Path.Combine(gameFolder, "Aventura.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        })!;
        try
        {
            await process.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("True,True,True", File.ReadAllText(Path.Combine(gameFolder, "abriu.txt")));
            Assert.Equal(GameSaveIdentityTests.Id(created.ProjectPath), File.ReadAllText(Path.Combine(gameFolder, "identidade.txt")));
            Assert.Equal("1", File.ReadAllText(Path.Combine(gameFolder, "aberturas.txt")));
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }

        var renamedTitle = new ProcessStartInfo(Path.Combine(gameFolder, "Aventura.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        renamedTitle.ArgumentList.Add("Título atualizado");
        using var second = Process.Start(renamedTitle)!;
        try
        {
            await second.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, second.ExitCode);
            Assert.Equal("2", File.ReadAllText(Path.Combine(gameFolder, "aberturas.txt")));
        }
        finally { if (!second.HasExited) { second.Kill(entireProcessTree: true); await second.WaitForExitAsync(); } }
    }
}
