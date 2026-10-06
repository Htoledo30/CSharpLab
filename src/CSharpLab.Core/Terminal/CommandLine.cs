using System.Text;

namespace CSharpLab.Core.Terminal;

/// <summary>
/// Monta a linha de comando de CreateProcess a partir de argumentos estruturados,
/// seguindo as regras de CommandLineToArgvW. Não passa por nenhum shell.
/// </summary>
public static class CommandLine
{
    public static string Build(string fileName, IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        // argv[0] não aceita escapes; caminhos de executável não contêm aspas.
        sb.Append('"').Append(fileName).Append('"');
        foreach (var arg in arguments)
        {
            sb.Append(' ');
            AppendArgument(sb, arg);
        }
        return sb.ToString();
    }

    public static void AppendArgument(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        for (int i = 0; ; i++)
        {
            int backslashes = 0;
            while (i < arg.Length && arg[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == arg.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }

            if (arg[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(arg[i]);
            }
        }
        sb.Append('"');
    }
}
