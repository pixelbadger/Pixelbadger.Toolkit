using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Components;

internal static class StringTokenPatterns
{
    internal static readonly Regex WordRegex = new Regex("[A-Za-z]+(?:'[A-Za-z]+)?", RegexOptions.Compiled);
}
