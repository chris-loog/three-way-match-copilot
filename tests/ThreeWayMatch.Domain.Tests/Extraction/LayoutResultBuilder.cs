using System.Text;
using System.Text.Json.Nodes;

namespace ThreeWayMatch.Domain.Tests.Extraction;

/// <summary>
/// Builds a <c>prebuilt-layout</c> result in the shape the service returns (content, words with spans and confidence, tables with cell spans)
/// from plain text, so mapper tests can describe a delivery note in a few lines. There is no real delivery-note result yet:
/// replace these with saved ones once the first <c>extract --type delivery-note</c> run exists.
/// </summary>
internal static class LayoutResultBuilder
{
    public static string Build(string header, string[][] table, string footer, IReadOnlyDictionary<string, double>? confidenceByWord = null)
    {
        var content = new StringBuilder(header).Append('\n');
        var cells = new JsonArray();
        for (var row = 0; row < table.Length; row++)
        {
            for (var column = 0; column < table[row].Length; column++)
            {
                var offset = content.Length;
                content.Append(table[row][column]).Append('\n');
                cells.Add(new JsonObject
                {
                    ["kind"] = row == 0 ? "columnHeader" : "content",
                    ["rowIndex"] = row,
                    ["columnIndex"] = column,
                    ["content"] = table[row][column],
                    ["spans"] = new JsonArray(new JsonObject { ["offset"] = offset, ["length"] = table[row][column].Length }),
                });
            }
        }

        content.Append(footer);
        var text = content.ToString();

        var words = new JsonArray();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isSpace = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!isSpace && start < 0)
            {
                start = i;
            }
            else if (isSpace && start >= 0)
            {
                var word = text[start..i];
                words.Add(new JsonObject
                {
                    ["content"] = word,
                    ["span"] = new JsonObject { ["offset"] = start, ["length"] = word.Length },
                    ["confidence"] = confidenceByWord is not null && confidenceByWord.TryGetValue(word, out var confidence) ? confidence : 0.99,
                });
                start = -1;
            }
        }

        return new JsonObject
        {
            ["content"] = text,
            ["pages"] = new JsonArray(new JsonObject { ["pageNumber"] = 1, ["words"] = words }),
            ["tables"] = new JsonArray(new JsonObject { ["rowCount"] = table.Length, ["columnCount"] = table[0].Length, ["cells"] = cells }),
            ["documents"] = new JsonArray(),
        }.ToJsonString();
    }
}
