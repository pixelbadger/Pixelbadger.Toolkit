namespace Pixelbadger.Toolkit.Components;

/// <summary>Output format of the rag actions. Lower-case member names so <c>--format text|json</c> parses and the MCP enum is [text, json].</summary>
public enum RagOutputFormat
{
    text,
    json
}
