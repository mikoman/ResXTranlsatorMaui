using OfficeOpenXml;
using System.Collections.Generic;
using System.IO;

namespace ResXTranslator
{
    public class ExcelGenerator
    {
        public void WriteResXToExcel(string excelPath, Dictionary<string, string> resxValues)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            // Create a new Excel package with a new workbook and worksheet
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("ResX Data");

                // Set headers
                worksheet.Cells[1, 1].Value = "CodeValue(For dev reference)";
                worksheet.Cells[1, 2].Value = "Value for Tanslation";

                int row = 2; // Start on the second row as the first row is for headers

                // Write data from ResX to Excel
                foreach (var item in resxValues)
                {
                    worksheet.Cells[row, 1].Value = item.Key;
                    worksheet.Cells[row, 2].Value = item.Value;
                    row++;
                }

                // Save the Excel file
                using (var stream = new FileStream(excelPath, FileMode.Create))
                {
                    package.SaveAs(stream);
                }
            }
        }
    }
}
