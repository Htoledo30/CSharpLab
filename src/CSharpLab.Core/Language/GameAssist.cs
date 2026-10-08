using CSharpLab.Core.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpLab.Core.Language;

/// <summary>O texto entre as aspas de um game.Find("…") onde está o cursor, e a cena em que ele está.</summary>
/// <param name="Start">Onde começa o nome (logo depois da aspa).</param>
/// <param name="Typed">O que já foi digitado do nome até o cursor.</param>
public sealed record FindNameContext(string? Scene, int Start, string Typed);

/// <summary>Uma cena do código: game.Scene("Nome", () => { … }).</summary>
/// <param name="NameSpan">O texto "Nome", com as aspas.</param>
/// <param name="Span">A chamada inteira, do game até o ")" final.</param>
public sealed record SceneCall(string Name, Microsoft.CodeAnalysis.Text.TextSpan NameSpan, Microsoft.CodeAnalysis.Text.TextSpan Span);

/// <summary>Um game.Find("Nome") no código; <paramref name="IsHandler"/>: ali o evento da peça é ligado (OnClick/OnAnswer).</summary>
public sealed record PieceUse(int Start, bool IsHandler);

/// <summary>Texto para pôr no código no lugar de [Offset, Offset+Length), e onde o cursor fica depois (relativo ao começo do texto).</summary>
public sealed record TextInsertion(int Offset, int Length, string Text, int Caret);

/// <summary>O nome de cena entre aspas sob o cursor: no próprio game.Scene ou num game.GoTo/game.Start.</summary>
public sealed record SceneReference(string Name, bool IsDeclaration);

/// <summary>
/// Ajuda do editor para as telas desenhadas: sugerir os nomes das peças dentro de game.Find("…")
/// e avisar, antes de rodar, quando um nome não existe na tela da cena.
/// </summary>
public static class GameAssist
{
    public const string UnknownPieceId = "DICA07";

    /// <summary>Se o cursor está dentro das aspas de um game.Find("…"), o contexto; senão null.</summary>
    public static FindNameContext? FindNameAt(SyntaxNode root, int position)
    {
        var token = root.FindToken(Math.Max(0, position - 1));
        if (!token.IsKind(SyntaxKind.StringLiteralToken) || token.Text.StartsWith('@')) return null;
        int start = token.SpanStart + 1;
        bool closed = token.Text.Length >= 2 && token.Text.EndsWith('"') && !token.IsMissing;
        int end = closed ? token.Span.End - 1 : token.Span.End;
        if (position < start || position > end) return null;
        if (token.Parent is not LiteralExpressionSyntax literal || FindCall(literal) is not { } call) return null;
        var typed = token.Text.Substring(1, position - start);
        return new FindNameContext(SceneOf(call), start, typed);
    }

    /// <summary>A chamada .Find(...) da qual o literal é o primeiro argumento.</summary>
    private static InvocationExpressionSyntax? FindCall(LiteralExpressionSyntax literal) =>
        literal.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list } argument &&
        list.Arguments.IndexOf(argument) == 0 &&
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Find" }
            ? call
            : null;

    /// <summary>O nome da cena: o primeiro argumento do game.Scene("Nome", () => { … }) que contém o código.</summary>
    public static string? SceneOf(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Scene" },
                    ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax { Token.ValueText: var scene } first }, var body, ..],
                } && first.IsKind(SyntaxKind.StringLiteralExpression) && body.Span.Contains(node.Span))
            {
                return scene;
            }
        }
        return null;
    }

    /// <summary>As cenas escritas no código, na ordem do arquivo.</summary>
    public static IReadOnlyList<SceneCall> FindScenes(SyntaxNode root)
    {
        var scenes = new List<SceneCall>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call is
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Scene" },
                    ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax first }, _, ..],
                } && first.IsKind(SyntaxKind.StringLiteralExpression) && first.Token.ValueText.Length > 0)
            {
                scenes.Add(new SceneCall(first.Token.ValueText, first.Span, call.Span));
            }
        }
        return scenes;
    }

    /// <summary>A cena cujo código contém a posição (a mais de dentro, se houver uma dentro da outra).</summary>
    public static SceneCall? SceneAt(SyntaxNode root, int position) =>
        FindScenes(root)
            .Where(s => position >= s.Span.Start && position <= s.Span.End)
            .OrderBy(s => s.Span.Length)
            .FirstOrDefault();

    /// <summary>
    /// O nome de cena entre aspas na posição: o "Fight" de game.Scene("Fight", …) (IsDeclaration)
    /// ou de game.GoTo("Fight") e game.Start("Fight").
    /// </summary>
    public static SceneReference? SceneReferenceAt(SyntaxNode root, int position)
    {
        if (position < 0 || position > root.FullSpan.End) return null;
        var token = root.FindToken(Math.Min(position, Math.Max(0, root.FullSpan.End - 1)));
        if (!token.IsKind(SyntaxKind.StringLiteralToken) || position < token.SpanStart || position > token.Span.End) return null;
        if (token.Parent is not LiteralExpressionSyntax literal ||
            literal.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list } argument ||
            list.Arguments.IndexOf(argument) != 0 ||
            call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method } ||
            token.ValueText.Length == 0)
            return null;
        return method switch
        {
            "Scene" when list.Arguments.Count >= 2 => new SceneReference(token.ValueText, true),
            "GoTo" or "Start" when list.Arguments.Count == 1 => new SceneReference(token.ValueText, false),
            _ => null,
        };
    }

    /// <summary>
    /// Onde o código usa a peça: o nome entre as aspas de cada game.Find("nome") dentro da cena.
    /// Peças com o mesmo nome em outras cenas não entram.
    /// </summary>
    public static IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> FindPieceReferences(SyntaxNode root, string scene, string name)
    {
        var spans = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Find" } ||
                call.ArgumentList.Arguments is not [{ Expression: LiteralExpressionSyntax literal }] ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.Token.Text.StartsWith('@') ||
                !string.Equals(literal.Token.ValueText, name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(SceneOf(call), scene, StringComparison.OrdinalIgnoreCase))
                continue;
            spans.Add(new Microsoft.CodeAnalysis.Text.TextSpan(literal.Token.SpanStart + 1, literal.Token.Text.Length - 2));
        }
        return spans;
    }

    /// <summary>
    /// Cada game.Find("nome") da peça dentro da cena, dizendo se ali o código liga o evento dela
    /// (ex.: game.Find("Attack").OnClick(...)).
    /// </summary>
    public static IReadOnlyList<PieceUse> FindPieceUses(SyntaxNode root, string scene, string name, string handler)
    {
        var uses = new List<PieceUse>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Find" } ||
                call.ArgumentList.Arguments is not [{ Expression: LiteralExpressionSyntax literal }] ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression) ||
                !string.Equals(literal.Token.ValueText, name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(SceneOf(call), scene, StringComparison.OrdinalIgnoreCase))
                continue;
            bool isHandler = call.Parent is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == handler &&
                             access.Parent is InvocationExpressionSyntax;
            uses.Add(new PieceUse(call.SpanStart, isHandler));
        }
        return uses;
    }

    /// <summary>
    /// Como escrever game.Find("nome").OnClick(() => { }); no fim do código da cena, com o recuo certo.
    /// Null se a cena não existe no código ou não tem chaves (escrita numa linha só).
    /// </summary>
    /// <param name="parameter">O que vem antes do "=>": "()" no OnClick, "answer" no OnAnswer.</param>
    public static TextInsertion? HandlerInsertion(SyntaxNode root, string scene, string name, string handler, string parameter, string newLine)
    {
        var calls = FindScenes(root);
        var target = calls.FirstOrDefault(s => s.Name == scene) ?? calls.FirstOrDefault(s => string.Equals(s.Name, scene, StringComparison.OrdinalIgnoreCase));
        if (target == null ||
            root.FindNode(target.Span) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: var receiver },
                ArgumentList.Arguments: [_, { Expression: LambdaExpressionSyntax { Block: { } block } }, ..],
            })
            return null;

        var text = root.SyntaxTree.GetText();
        string IndentOf(int position)
        {
            var line = text.Lines.GetLineFromPosition(position);
            var content = text.ToString(line.Span);
            return content[..(content.Length - content.TrimStart(' ', '\t').Length)];
        }
        string braceIndent = IndentOf(block.OpenBraceToken.SpanStart);
        // O recuo das instruções da cena (ou um nível a mais que o "{", se a cena está vazia ou numa linha só).
        bool firstOnOwnLine = block.Statements.Count > 0 &&
                              text.Lines.GetLineFromPosition(block.Statements[0].SpanStart).LineNumber != text.Lines.GetLineFromPosition(block.OpenBraceToken.SpanStart).LineNumber;
        string inner = firstOnOwnLine ? IndentOf(block.Statements[0].SpanStart) : braceIndent + "    ";

        string head = $"{inner}{receiver}.Find(\"{name}\").{handler}({parameter} =>{newLine}{inner}{{{newLine}{inner}    ";
        string tail = $"{newLine}{inner}}});";
        string separator = block.Statements.Count > 0 ? newLine : "";

        var close = block.CloseBraceToken.SpanStart;
        var closeLine = text.Lines.GetLineFromPosition(close);
        bool braceAlone = text.ToString(TextSpan.FromBounds(closeLine.Start, close)).Trim().Length == 0;
        if (braceAlone)
        {
            // Entra numa linha nova logo antes do "});" da cena.
            var insert = separator + head + tail + newLine;
            return new TextInsertion(closeLine.Start, 0, insert, separator.Length + head.Length);
        }
        // "{ }" ou "{ game.Write(...); }" na mesma linha: o "}" desce para a linha de baixo
        // (os espaços antes dele saem junto).
        int from = block.CloseBraceToken.GetPreviousToken().Span.End;
        var inline = newLine + separator + head + tail + newLine + braceIndent;
        return new TextInsertion(from, close - from, inline, newLine.Length + separator.Length + head.Length);
    }

    public const string BuildInDrawnSceneId = "DICA08";

    private static readonly Dictionary<string, string> BuildMethods = new(StringComparer.Ordinal)
    {
        ["Title"] = "um Texto",
        ["Button"] = "um Botão",
        ["Bar"] = "uma Barra",
        ["Ask"] = "um Campo de escrita",
        ["Image"] = "uma Imagem",
    };

    /// <summary>
    /// game.Title/Button/Bar/Ask/Image numa cena que tem tela desenhada: lá as peças vêm da tela, e esses
    /// comandos dão erro ao rodar. O aviso aparece antes, dizendo o que fazer.
    /// </summary>
    public static IEnumerable<(string Id, string Message, Location Location)> CheckBuildCallsInDrawnScenes(SyntaxNode root, string projectDirectory)
    {
        var scenes = FindScenes(root);
        if (scenes.Count == 0) yield break;
        var drawn = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool IsDrawn(string scene)
        {
            if (!drawn.TryGetValue(scene, out var value))
            {
                var path = GameScreens.PathOf(projectDirectory, scene);
                drawn[scene] = value = GameScreens.OpenTexts.ContainsKey(path) || File.Exists(path);
            }
            return value;
        }

        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method, Expression: var receiver } access ||
                !BuildMethods.TryGetValue(method, out var piece))
                continue;
            var scene = SceneOf(call);
            if (scene == null || !IsDrawn(scene)) continue;
            // Só o "game" que criou a cena (não um método com o mesmo nome numa classe sua).
            var sceneCall = scenes.First(s => s.Name == scene || string.Equals(s.Name, scene, StringComparison.OrdinalIgnoreCase));
            if (root.FindNode(sceneCall.Span) is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: var game } } ||
                game.ToString() != receiver.ToString())
                continue;
            yield return (BuildInDrawnSceneId,
                $"A cena \"{scene}\" tem tela desenhada, então o {receiver}.{method} não funciona nela. " +
                $"Ponha {piece} na aba Tela e mude pelo código com {receiver}.Find(\"Nome\").",
                access.Name.GetLocation());
        }
    }

    /// <summary>Avisos para game.Find com nome que não existe na tela da cena (ou cena sem tela desenhada).</summary>
    public static IEnumerable<(string Id, string Message, Location Location)> CheckFindNames(SyntaxNode root, string projectDirectory)
    {
        var cache = new Dictionary<string, IReadOnlyList<ScreenPiece>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Find" } ||
                call.ArgumentList.Arguments is not [{ Expression: LiteralExpressionSyntax literal }] ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
                continue;
            var scene = SceneOf(call);
            if (scene == null) continue;
            if (!cache.TryGetValue(scene, out var pieces)) cache[scene] = pieces = GameScreens.Pieces(projectDirectory, scene);

            var name = literal.Token.ValueText;
            if (pieces == null)
            {
                // Só avisa se o jogo já usa telas desenhadas e o arquivo desta cena não existe.
                if (!File.Exists(GameScreens.PathOf(projectDirectory, scene)) && !GameScreens.OpenTexts.ContainsKey(GameScreens.PathOf(projectDirectory, scene)))
                {
                    yield return (UnknownPieceId,
                        $"A cena \"{scene}\" não tem tela desenhada, então o game.Find não acha peças. Crie a tela em Arquivo → Nova tela do jogo…",
                        literal.GetLocation());
                }
                continue;
            }
            if (pieces.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

            var guess = pieces.Select(p => p.Name)
                .Where(n => Distance(n.ToLowerInvariant(), name.ToLowerInvariant()) <= 2)
                .OrderBy(n => Distance(n.ToLowerInvariant(), name.ToLowerInvariant()))
                .FirstOrDefault();
            var hint = guess != null ? $" Você quis dizer \"{guess}\"?" : "";
            var list = pieces.Count > 0 ? " Peças: " + string.Join(", ", pieces.Take(12).Select(p => p.Name)) + "." : "";
            yield return (UnknownPieceId,
                $"A peça \"{name}\" não existe na tela \"{scene}\".{hint}{list}",
                literal.GetLocation());
        }
    }

    internal static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int previous = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int temp = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = temp;
            }
        }
        return row[b.Length];
    }
}
