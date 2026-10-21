using System.Text;
using System.Text.Json;

namespace RequestExplorer.Services.CaretPath;

/// <summary>
/// Maps a UTF-16 caret index in a JSON document to a JSONPath such as <c>$.items[2].name</c>.
/// </summary>
public static class JsonCaretPath
{
    public static bool TryGetPath(string? json, int caretCharIndex, out string path)
    {
        path = "$";
        if (string.IsNullOrWhiteSpace(json) || !LooksLikeJson(json))
        {
            return false;
        }

        caretCharIndex = Math.Clamp(caretCharIndex, 0, json.Length);
        var utf8 = Encoding.UTF8.GetBytes(json);
        var caretByte = Encoding.UTF8.GetByteCount(json.AsSpan(0, caretCharIndex));

        var reader = new Utf8JsonReader(
            utf8,
            new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        var stack = new List<Frame>();
        try
        {
            while (reader.Read())
            {
                var start = reader.TokenStartIndex;
                var end = reader.BytesConsumed;

                if (caretByte < start)
                {
                    path = Build(stack);
                    return true;
                }

                var insideToken = caretByte < end;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        EnterValue(stack);
                        stack.Add(new Frame { IsArray = false });
                        if (insideToken)
                        {
                            path = Build(stack);
                            return true;
                        }

                        break;

                    case JsonTokenType.StartArray:
                        EnterValue(stack);
                        stack.Add(new Frame { IsArray = true, Index = -1 });
                        if (insideToken)
                        {
                            path = Build(stack);
                            return true;
                        }

                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (insideToken)
                        {
                            if (stack.Count > 0 && stack[^1].IsArray)
                            {
                                stack[^1].Index = -1;
                            }

                            path = Build(stack);
                            return true;
                        }

                        ExitContainer(stack);
                        break;

                    case JsonTokenType.PropertyName:
                    {
                        if (stack.Count == 0 || stack[^1].IsArray)
                        {
                            return false;
                        }

                        stack[^1].Name = reader.GetString();
                        if (insideToken)
                        {
                            path = Build(stack);
                            return true;
                        }

                        break;
                    }

                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        EnterValue(stack);
                        if (insideToken || caretByte == end && caretCharIndex == json.Length)
                        {
                            path = Build(stack);
                            return true;
                        }

                        FinishLeaf(stack);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        path = Build(stack);
        return true;
    }

    public static bool LooksLikeJson(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '{' or '[';
    }

    private static void EnterValue(List<Frame> stack)
    {
        if (stack.Count == 0)
        {
            return;
        }

        var parent = stack[^1];
        if (parent.IsArray)
        {
            parent.Index++;
        }
    }

    private static void FinishLeaf(List<Frame> stack)
    {
        if (stack.Count == 0)
        {
            return;
        }

        var parent = stack[^1];
        if (!parent.IsArray)
        {
            parent.Name = null;
        }
    }

    private static void ExitContainer(List<Frame> stack)
    {
        if (stack.Count == 0)
        {
            return;
        }

        stack.RemoveAt(stack.Count - 1);
        FinishLeaf(stack);
    }

    private static string Build(List<Frame> stack)
    {
        var sb = new StringBuilder("$");
        foreach (var frame in stack)
        {
            if (frame.IsArray)
            {
                if (frame.Index >= 0)
                {
                    sb.Append('[').Append(frame.Index).Append(']');
                }
            }
            else if (!string.IsNullOrEmpty(frame.Name))
            {
                AppendProperty(sb, frame.Name);
            }
        }

        return sb.ToString();
    }

    private static void AppendProperty(StringBuilder sb, string name)
    {
        if (IsSimpleName(name))
        {
            sb.Append('.').Append(name);
            return;
        }

        sb.Append("[\"").Append(name.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\"]");
    }

    private static bool IsSimpleName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        var first = name[0];
        if (!char.IsLetter(first) && first != '_')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Frame
    {
        public bool IsArray { get; init; }
        public int Index { get; set; }
        public string? Name { get; set; }
    }
}
