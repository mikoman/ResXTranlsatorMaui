using System.Globalization;
using ClosedXML.Excel;

namespace ResXTranslator;

public sealed class TranslationSpreadsheetDocument : IDisposable
{
    const string DefaultHeader = "Default";
    const string DefaultFileHeader = "DefaultFile";
    const string DefaultEnglishHeader = "en-US";
    const string MetadataBoundaryHeader = "Missing";
    const string StatusSuffix = " Status";

    readonly XLWorkbook _workbook;
    readonly IXLWorksheet _worksheet;

    TranslationSpreadsheetDocument(XLWorkbook workbook, IXLWorksheet worksheet)
    {
        _workbook = workbook;
        _worksheet = worksheet;
        ValidateStructure();
    }

    public bool IsModified { get; private set; }

    public int EntryCount => Math.Max(0, GetLastRow() - 1);

    public IReadOnlyList<string> LanguageHeaders => GetLanguageColumns()
        .Select(pair => pair.Header)
        .ToArray();

    public static TranslationSpreadsheetDocument Load(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => LoadCsv(path),
            ".xlsx" => LoadExcel(path),
            _ => throw new NotSupportedException("Only .csv and .xlsx translation documents are supported.")
        };

    public bool HasLanguage(string languageHeader) =>
        GetLanguageColumns().Any(pair =>
            string.Equals(pair.Header, languageHeader, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<TranslationSourceRow> GetSourceRows()
    {
        var defaultColumn = FindHeaderColumn(DefaultHeader);
        var englishColumn = FindHeaderColumn(DefaultEnglishHeader, required: false);
        var lastRow = GetLastRow();
        var rows = new List<TranslationSourceRow>(Math.Max(0, lastRow - 1));

        for (var row = 2; row <= lastRow; row++)
        {
            var sourceText = GetCellValue(row, defaultColumn);

            if (string.IsNullOrWhiteSpace(sourceText) && englishColumn > 0)
            {
                sourceText = GetCellValue(row, englishColumn);
            }

            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                rows.Add(new TranslationSourceRow(row, sourceText));
            }
        }

        return rows;
    }

    public void AddLanguage(
        string languageHeader,
        IReadOnlyDictionary<int, string> translatedValues)
    {
        if (string.IsNullOrWhiteSpace(languageHeader))
        {
            throw new ArgumentException("A language header is required.", nameof(languageHeader));
        }

        if (HasLanguage(languageHeader))
        {
            throw new InvalidOperationException($"The document already contains a '{languageHeader}' translation column.");
        }

        var languageColumns = GetLanguageColumns();
        var lastLanguage = languageColumns[^1];
        var insertAt = lastLanguage.StatusColumn + 1;
        var valueWidth = _worksheet.Column(lastLanguage.ValueColumn).Width;
        var statusWidth = _worksheet.Column(lastLanguage.StatusColumn).Width;

        _worksheet.Column(insertAt).InsertColumnsBefore(2);
        _worksheet.Column(insertAt).Width = valueWidth;
        _worksheet.Column(insertAt + 1).Width = statusWidth;
        _worksheet.Cell(1, insertAt).Style = _worksheet.Cell(1, lastLanguage.ValueColumn).Style;
        _worksheet.Cell(1, insertAt + 1).Style = _worksheet.Cell(1, lastLanguage.StatusColumn).Style;
        _worksheet.Cell(1, insertAt).Value = languageHeader;
        _worksheet.Cell(1, insertAt + 1).Value = languageHeader + StatusSuffix;

        var lastRow = GetLastRow();

        foreach (var translation in translatedValues)
        {
            if (translation.Key < 2 || translation.Key > lastRow)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(translatedValues),
                    $"Row {translation.Key} does not exist in the document.");
            }

            _worksheet.Cell(translation.Key, insertAt).Value = translation.Value;
        }

        IsModified = true;
    }

    public void SaveAsExcel(string path)
    {
        EnsureOutputDirectory(path);
        _workbook.SaveAs(path);
    }

    public void SaveAsCsv(string path) => CsvFile.Write(path, EnumerateRows());

    public void Dispose() => _workbook.Dispose();

    static TranslationSpreadsheetDocument LoadCsv(string path)
    {
        var rows = CsvFile.Read(path);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The CSV file is empty.");
        }

        var columnCount = rows[0].Length;

        for (var row = 1; row < rows.Count; row++)
        {
            if (rows[row].Length != columnCount)
            {
                throw new FormatException(
                    $"CSV row {row + 1} has {rows[row].Length} columns; the header has {columnCount}.");
            }
        }

        var workbook = new XLWorkbook();

        try
        {
            var worksheet = workbook.Worksheets.Add("Translations");

            for (var row = 0; row < rows.Count; row++)
            {
                for (var column = 0; column < columnCount; column++)
                {
                    worksheet.Cell(row + 1, column + 1).Value = rows[row][column];
                }
            }

            return new TranslationSpreadsheetDocument(workbook, worksheet);
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    static TranslationSpreadsheetDocument LoadExcel(string path)
    {
        var workbook = new XLWorkbook(path);

        try
        {
            var worksheet = workbook.Worksheets
                .FirstOrDefault(IsTranslationWorksheet)
                ?? throw new InvalidOperationException(
                    "No worksheet with a 'Default' column and language/status column pairs was found.");

            return new TranslationSpreadsheetDocument(workbook, worksheet);
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    static bool IsTranslationWorksheet(IXLWorksheet worksheet)
    {
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        if (lastColumn == 0)
        {
            return false;
        }

        var headers = Enumerable.Range(1, lastColumn)
            .Select(column => worksheet.Cell(1, column).GetString().Trim())
            .ToArray();

        return headers.Contains(DefaultHeader, StringComparer.OrdinalIgnoreCase) &&
            headers.Any(header => header.EndsWith(StatusSuffix, StringComparison.OrdinalIgnoreCase));
    }

    void ValidateStructure()
    {
        _ = FindHeaderColumn(DefaultHeader);

        if (GetLanguageColumns().Count == 0)
        {
            throw new InvalidOperationException(
                "The document must contain at least one language column followed by its '<language> Status' column.");
        }
    }

    List<LanguageColumnPair> GetLanguageColumns()
    {
        var pairs = new List<LanguageColumnPair>();
        var defaultFileColumn = FindHeaderColumn(DefaultFileHeader, required: false);
        var firstColumn = defaultFileColumn > 0
            ? defaultFileColumn + 1
            : FindHeaderColumn(DefaultHeader) + 1;
        var metadataBoundaryColumn = FindHeaderColumn(MetadataBoundaryHeader, required: false);
        var lastColumn = metadataBoundaryColumn > 0
            ? metadataBoundaryColumn - 1
            : GetLastColumn();

        for (var column = firstColumn; column < lastColumn; column++)
        {
            var header = GetHeader(column);

            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var nextHeader = GetHeader(column + 1);

            if (string.Equals(nextHeader, header + StatusSuffix, StringComparison.OrdinalIgnoreCase))
            {
                pairs.Add(new LanguageColumnPair(header, column, column + 1));
                column++;
            }
        }

        return pairs;
    }

    int FindHeaderColumn(string header, bool required = true)
    {
        var lastColumn = GetLastColumn();

        for (var column = 1; column <= lastColumn; column++)
        {
            if (string.Equals(GetHeader(column), header, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        if (required)
        {
            throw new InvalidOperationException($"The document does not contain the required '{header}' column.");
        }

        return -1;
    }

    int GetLastRow() => _worksheet.LastRowUsed()?.RowNumber() ?? 1;

    int GetLastColumn() => _worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

    string GetHeader(int column) => _worksheet.Cell(1, column).GetString().Trim();

    string GetCellValue(int row, int column) => _worksheet.Cell(row, column).GetString();

    IEnumerable<IReadOnlyList<string>> EnumerateRows()
    {
        var lastRow = GetLastRow();
        var lastColumn = GetLastColumn();

        for (var row = 1; row <= lastRow; row++)
        {
            var values = new string[lastColumn];

            for (var column = 1; column <= lastColumn; column++)
            {
                values[column - 1] = _worksheet.Cell(row, column)
                    .GetFormattedString(CultureInfo.InvariantCulture);
            }

            yield return values;
        }
    }

    static void EnsureOutputDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    sealed record LanguageColumnPair(string Header, int ValueColumn, int StatusColumn);
}

public sealed record TranslationSourceRow(int RowNumber, string Text);
