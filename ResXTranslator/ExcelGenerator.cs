using ClosedXML.Excel;

namespace ResXTranslator;

public class ExcelGenerator
{
    public void WriteResXToExcel(string excelPath, Dictionary<string, string> resxValues)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(excelPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("ResX Data");

        worksheet.Cell(1, 1).Value = "CodeValue(For dev reference)";
        worksheet.Cell(1, 2).Value = "Value for Translation";
        worksheet.Range(1, 1, 1, 2).Style.Font.Bold = true;

        var row = 2;

        foreach (var item in resxValues)
        {
            worksheet.Cell(row, 1).Value = item.Key;
            worksheet.Cell(row, 2).Value = item.Value;
            row++;
        }

        worksheet.ColumnsUsed().AdjustToContents();
        workbook.SaveAs(excelPath);
    }

    public void WriteResXToCsv(string csvPath, Dictionary<string, string> resxValues)
    {
        var rows = resxValues
            .Select(item => (IReadOnlyList<string>)[item.Key, item.Value])
            .Prepend(["CodeValue(For dev reference)", "Value for Translation"]);

        CsvFile.Write(csvPath, rows);
    }
}
