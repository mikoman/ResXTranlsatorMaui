using System.Text;

namespace ResXTranslator;

static class CsvFile
{
    public static List<string[]> Read(string path)
    {
        var text = File.ReadAllText(path, new UTF8Encoding(false, true));

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();

                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("The CSV file ends inside a quoted value.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    public static void Write(string path, IEnumerable<IReadOnlyList<string>> rows)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Count; column++)
            {
                if (column > 0)
                {
                    writer.Write(',');
                }

                WriteField(writer, row[column]);
            }

            writer.Write("\r\n");
        }
    }

    static void WriteField(TextWriter writer, string value)
    {
        var needsQuotes = value.Contains(',') ||
            value.Contains('"') ||
            value.Contains('\r') ||
            value.Contains('\n');

        if (!needsQuotes)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }
}
