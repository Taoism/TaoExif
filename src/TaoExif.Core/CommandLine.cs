using System.Text;

namespace TaoExif.Core;

/// <summary>Splits a typed command line into arguments (double and single quotes group).</summary>
public static class CommandLine
{
    public static List<string> Split(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inArg = false;
        char quote = '\0';

        foreach (char c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else
                    current.Append(c);
                continue;
            }
            if (c is '"' or '\'')
            {
                quote = c;
                inArg = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inArg)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    inArg = false;
                }
            }
            else
            {
                current.Append(c);
                inArg = true;
            }
        }
        if (inArg)
            args.Add(current.ToString());
        return args;
    }
}
