using System.Diagnostics;

namespace CSharpLab.Tests;

/// <summary>Compila uma vez um programa console de teste numa pasta com espaços e acentos.</summary>
public sealed class SampleConsoleApp : IDisposable
{
    public const string Source = """
        var mode = args.Length > 0 ? args[0] : "readline";
        switch (mode)
        {
            case "readline":
                Console.Write("Nome: ");
                var nome = Console.ReadLine();
                Console.WriteLine($"Olá, {nome}! ✓");
                break;
            case "readkey":
                Console.Write("Tecla: ");
                var k = Console.ReadKey(true);
                Console.WriteLine($"[{k.Key}]");
                break;
            case "loop":
                Console.WriteLine("loop");
                while (true) { }
            case "child":
                var self = Environment.ProcessPath!;
                var p = System.Diagnostics.Process.Start(self, "loop");
                Console.WriteLine($"child={p.Id}");
                p.WaitForExit();
                break;
            case "throw":
                throw new InvalidOperationException("falhou");
            case "clear":
                Console.WriteLine("antes");
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("depois");
                Console.ResetColor();
                break;
            case "readkeyecho":
                Console.Write("> ");
                var e = Console.ReadKey(false);
                Console.WriteLine($" [{e.KeyChar}]");
                break;
            case "exit3":
                Console.Write("fim sem quebra");
                return 3;
        }
        return 0;
        """;

    public string Directory { get; }
    public string ProjectPath { get; }
    public string ExePath { get; }

    public SampleConsoleApp()
    {
        Directory = Path.Combine(Path.GetTempPath(), "csharplab-tests", Guid.NewGuid().ToString("N")[..8], "App Ação");
        System.IO.Directory.CreateDirectory(Directory);
        ProjectPath = Path.Combine(Directory, "AppAcao.csproj");
        File.WriteAllText(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Directory, "Program.cs"), Source);

        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "build", ProjectPath, "-nologo", "-v:q" }) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException("Falha ao compilar app de teste:\n" + output);
        ExePath = Path.Combine(Directory, "bin", "Debug", "net10.0", "AppAcao.exe");
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Path.GetDirectoryName(Directory)!, true); } catch { }
    }
}

[CollectionDefinition("SampleApp")]
public sealed class SampleAppCollection : ICollectionFixture<SampleConsoleApp>;
