using System.Net;
using System.Text.RegularExpressions;

namespace CSharpLab.Core.Language;

/// <summary>Página da Microsoft, em português, que explica um erro ou aviso do compilador.</summary>
public static partial class HelpLinks
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };

    public static bool HasHelp(string diagnosticId) =>
        CompilerId().IsMatch(diagnosticId) || diagnosticId == RuntimeErrors.DiagnosticId;

    /// <summary>
    /// O serviço de ajuda da Microsoft sabe onde fica a página de cada código (CS0103, CS8600…),
    /// mas responde sempre com o endereço em inglês. A mesma página existe em pt-br.
    /// Sem internet, cai na busca em português.
    /// </summary>
    public static async Task<string> ForCompilerAsync(string id, CancellationToken ct = default)
    {
        var fallback = $"https://learn.microsoft.com/pt-br/search/?terms={Uri.EscapeDataString(id)}";
        try
        {
            using var response = await Http.GetAsync($"https://msdn.microsoft.com/query/roslyn.query?appId=roslyn&k=k({id})", ct).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)
                || response.Headers.Location is not { } location)
                return fallback;
            return ToPortuguese(location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri("https://learn.microsoft.com"), location).ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return fallback;
        }
    }

    /// <summary>Troca o idioma do endereço por pt-br e tira o rastreio da ajuda.</summary>
    public static string ToPortuguese(string url)
    {
        var uri = new Uri(url);
        var path = LocalePrefix().Replace(uri.AbsolutePath, "/pt-br/", 1);
        if (!path.StartsWith("/pt-br/", StringComparison.Ordinal)) path = "/pt-br" + path;
        return $"https://{uri.Host}{path}";
    }

    [GeneratedRegex(@"^CS\d{4}$")]
    private static partial Regex CompilerId();

    [GeneratedRegex(@"^/[a-z]{2}-[a-z]{2}/", RegexOptions.IgnoreCase)]
    private static partial Regex LocalePrefix();
}
