using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CSharpLab.Core.Files;

namespace CSharpLab.Core.Projects;

/// <summary>Um identificador permanente no projeto, também presente no executável exportado.</summary>
internal static class GameSaveIdentity
{
    internal const string MetadataName = "CSharpLab.SaveId";

    internal static void Ensure(string projectPath)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectPath).ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\CSharpLab.GameId." + key);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new UserFacingException("Outro editor está preparando este jogo. Espere ele terminar e tente novamente.");
        try
        {
            var file = TextFileIO.Read(projectPath);
            var document = XDocument.Parse(file.Text, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new UserFacingException("O arquivo do projeto está vazio.");
            var identities = root.Descendants().Where(e => e.Name.LocalName == "AssemblyMetadata" &&
                string.Equals((string?)e.Attribute("Include"), MetadataName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (identities.Count > 1)
                throw new UserFacingException("O projeto tem mais de um identificador de salvamento. Mantenha apenas um AssemblyMetadata chamado CSharpLab.SaveId.");
            if (identities.Count == 1)
            {
                var identity = identities[0];
                if (identity.AncestorsAndSelf().Any(e => e.Attribute("Condition") != null) ||
                    !Guid.TryParse((string?)identity.Attribute("Value"), out _))
                    throw new UserFacingException("O identificador de salvamento do projeto precisa ser um GUID fixo, sem Condition. Preserve o identificador original para continuar usando os saves.");
                return; // Nunca trocar uma identidade existente ao executar ou exportar.
            }

            var newline = TextFileIO.DetectLineEnding(file.Text) == "LF" ? "\n" : "\r\n";
            var ns = root.Name.Namespace;
            var group = new XElement(ns + "ItemGroup",
                new XText(newline + "    "),
                new XComment(" Identificador dos saves: preserve este valor ao renomear ou exportar o jogo. "),
                new XText(newline + "    "),
                new XElement(ns + "AssemblyMetadata", new XAttribute("Include", MetadataName), new XAttribute("Value", Guid.NewGuid().ToString("N"))),
                new XText(newline + "  "));
            root.Add(new XText(newline + "  "), group, new XText(newline));
            // Mantém comentários, codificação e finais de linha do projeto existente.
            var text = document.ToString(SaveOptions.DisableFormatting);
            text = text.Replace("\r\n", "\n").Replace("\n", newline);
            if (document.Declaration != null) text = document.Declaration + newline + text;
            TextFileIO.Save(projectPath, text, file.Encoding);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
