namespace Pixelbadger.Toolkit.Utilities;

public static class XmlUtility
{
    public static string EscapeXml(string input) =>
        input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
