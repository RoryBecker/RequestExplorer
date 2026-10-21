using System.IO;
using System.Text;
using System.Xml;

namespace RequestExplorer.Services.CaretPath;

/// <summary>
/// Maps a UTF-16 caret index in an XML document to an XPath such as
/// <c>/root/items/item[2]/@id</c>.
/// </summary>
public static class XmlCaretPath
{
    public static bool TryGetPath(string? xml, int caretCharIndex, out string path)
    {
        path = "/";
        if (string.IsNullOrWhiteSpace(xml) || !LooksLikeXml(xml))
        {
            return false;
        }

        caretCharIndex = Math.Clamp(caretCharIndex, 0, xml.Length);
        CharIndexToLinePos(xml, caretCharIndex, out var caretLine, out var caretCol);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false
        };

        var stack = new List<Frame>();
        string? fallbackPath = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var info = (IXmlLineInfo)reader;

            while (reader.Read())
            {
                if (!info.HasLineInfo())
                {
                    continue;
                }

                // Only stop once the reader has moved past the caret. Earlier nodes
                // (e.g. the first <title>) must not claim every later position.
                if (IsPastCaret(info, reader.NodeType, caretLine, caretCol))
                {
                    path = fallbackPath ?? Build(stack);
                    return path.Length > 1;
                }

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                    {
                        var name = QualifiedName(reader);
                        EnterElement(stack, name);
                        if (TryAttributes(reader, info, caretLine, caretCol, stack, out path, out _))
                        {
                            return true;
                        }

                        fallbackPath = Build(stack);
                        if (reader.IsEmptyElement)
                        {
                            stack.RemoveAt(stack.Count - 1);
                            // Keep fallback on this empty element so `<item/>` still maps here.
                        }

                        break;
                    }

                    case XmlNodeType.EndElement:
                        fallbackPath = Build(stack);
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }

                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        if (stack.Count > 0)
                        {
                            fallbackPath = Build(stack);
                        }

                        break;
                }
            }
        }
        catch (XmlException)
        {
            return false;
        }

        path = fallbackPath ?? Build(stack);
        return path.Length > 1;
    }

    public static bool LooksLikeXml(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '<';
    }

    private static bool TryAttributes(
        XmlReader reader,
        IXmlLineInfo info,
        int caretLine,
        int caretCol,
        List<Frame> stack,
        out string path,
        out string? lastAttributePath)
    {
        path = "/";
        lastAttributePath = null;
        if (!reader.MoveToFirstAttribute())
        {
            return false;
        }

        try
        {
            do
            {
                if (!info.HasLineInfo())
                {
                    continue;
                }

                if (IsPastCaret(info, XmlNodeType.Attribute, caretLine, caretCol))
                {
                    path = lastAttributePath ?? Build(stack);
                    return path.Length > 1;
                }

                lastAttributePath = Build(stack) + "/@" + AttributeName(reader);
            }
            while (reader.MoveToNextAttribute());
        }
        finally
        {
            reader.MoveToElement();
        }

        return false;
    }

    private static void EnterElement(List<Frame> stack, string name)
    {
        var index = 1;
        if (stack.Count > 0)
        {
            var parent = stack[^1];
            parent.ChildCounts.TryGetValue(name, out var seen);
            index = seen + 1;
            parent.ChildCounts[name] = index;
        }

        stack.Add(new Frame { Name = name, Index = index });
    }

    private static string QualifiedName(XmlReader reader) =>
        string.IsNullOrEmpty(reader.Prefix) ? reader.LocalName : reader.Prefix + ":" + reader.LocalName;

    private static string AttributeName(XmlReader reader) =>
        string.IsNullOrEmpty(reader.Prefix) ? reader.LocalName : reader.Prefix + ":" + reader.LocalName;

    private static string Build(List<Frame> stack)
    {
        if (stack.Count == 0)
        {
            return "/";
        }

        var sb = new StringBuilder();
        foreach (var frame in stack)
        {
            sb.Append('/').Append(frame.Name);
            if (frame.Index > 1)
            {
                sb.Append('[').Append(frame.Index).Append(']');
            }
        }

        return sb.ToString();
    }

    private static bool IsPastCaret(IXmlLineInfo info, XmlNodeType nodeType, int caretLine, int caretCol)
    {
        if (info.LineNumber > caretLine)
        {
            return true;
        }

        if (info.LineNumber < caretLine)
        {
            return false;
        }

        // XmlReader reports Element/EndElement at the name, one column after `<`.
        var startCol = info.LinePosition;
        if (nodeType is XmlNodeType.Element or XmlNodeType.EndElement)
        {
            startCol = Math.Max(1, startCol - 1);
        }

        return startCol > caretCol;
    }

    private static void CharIndexToLinePos(string text, int charIndex, out int line, out int column)
    {
        line = 1;
        column = 1;
        for (var i = 0; i < charIndex && i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < charIndex && text[i + 1] == '\n')
                {
                    continue;
                }

                line++;
                column = 1;
            }
            else if (c == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
    }

    private sealed class Frame
    {
        public required string Name { get; init; }
        public int Index { get; init; }
        public Dictionary<string, int> ChildCounts { get; } = new(StringComparer.Ordinal);
    }
}
