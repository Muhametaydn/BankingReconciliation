using System.Globalization;
using System.IO.Compression;
using System.Text;
using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public class ReconciliationExcelReportExporter : IReconciliationExcelReportExporter
{
    public byte[] ExportDifferences(ReconciliationBatch batch)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", CreateContentTypesXml());
            AddEntry(archive, "_rels/.rels", CreateRootRelationshipsXml());
            AddEntry(archive, "xl/workbook.xml", CreateWorkbookXml());
            AddEntry(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationshipsXml());
            AddEntry(archive, "xl/worksheets/sheet1.xml", CreateWorksheetXml(batch));
        }

        return stream.ToArray();
    }

    private static string CreateWorksheetXml(ReconciliationBatch batch)
    {
        var fieldDifferenceNames = batch.Summary.Results
            .SelectMany(result => result.FieldDifferences.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var headers = new List<string>
        {
            "Status",
            "BranchCode",
            "FundCode",
            "TransactionNumber",
            "BranchQuantity",
            "BankQuantity",
            "QuantityDifference",
            "BranchAmount",
            "BankAmount",
            "AmountDifference"
        };
        headers.AddRange(fieldDifferenceNames.Select(field => $"{field}Difference"));
        headers.Add("DifferenceNote");

        var rows = new List<IReadOnlyList<string>>
        {
            headers
        };

        rows.AddRange(batch.Summary.Results
            .Where(result => result.Status != ReconciliationStatus.Matched)
            .Select(result => new List<string>
            {
                result.Status.ToString(),
                result.BranchCode,
                result.FundCode,
                result.TransactionNumber,
                FormatDecimal(result.BranchRecord?.Quantity),
                FormatDecimal(result.BankRecord?.Quantity),
                FormatDecimal(result.QuantityDifference),
                FormatDecimal(result.BranchRecord?.Amount),
                FormatDecimal(result.BankRecord?.Amount),
                FormatDecimal(result.AmountDifference)
            }
                .Concat(fieldDifferenceNames.Select(field =>
                    result.FieldDifferences.TryGetValue(field, out var difference)
                        ? FormatDecimal(difference)
                        : string.Empty))
                .Append(CreateDifferenceNote(result))
                .ToArray()));

        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.Append("""<sheetData>""");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex + 1}\">");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var cellReference = $"{GetColumnName(columnIndex + 1)}{rowIndex + 1}";
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellReference}\" t=\"inlineStr\"><is><t>");
                builder.Append(XmlEscape(rows[rowIndex][columnIndex]));
                builder.Append("</t></is></c>");
            }

            builder.Append("</row>");
        }

        builder.Append("""</sheetData>""");
        builder.Append("""</worksheet>""");

        return builder.ToString();
    }

    private static string CreateDifferenceNote(ReconciliationResult result)
    {
        return result.Status switch
        {
            ReconciliationStatus.OnlyInBranch => "Sadece sube tarafinda var.",
            ReconciliationStatus.OnlyInBank => "Sadece banka tarafinda var.",
            ReconciliationStatus.QuantityMismatch => CreateQuantityNote(result.QuantityDifference),
            ReconciliationStatus.AmountMismatch => CreateAmountNote(result.AmountDifference),
            ReconciliationStatus.QuantityAndAmountMismatch =>
                $"{CreateQuantityNote(result.QuantityDifference)} {CreateAmountNote(result.AmountDifference)}",
            ReconciliationStatus.FieldMismatch => CreateFieldMismatchNote(result.FieldDifferences),
            _ => "Fark yok."
        };
    }

    private static string CreateFieldMismatchNote(IReadOnlyDictionary<string, decimal> fieldDifferences)
    {
        return fieldDifferences.Count == 0
            ? "Ek alan farki var."
            : $"Ek alan farki var: {string.Join(", ", fieldDifferences.Select(difference => $"{difference.Key}={difference.Value.ToString(CultureInfo.InvariantCulture)}"))}.";
    }

    private static string CreateQuantityNote(decimal? difference)
    {
        return difference switch
        {
            > 0 => "Adet sube tarafinda fazla gorunuyor.",
            < 0 => "Adet banka tarafinda fazla gorunuyor.",
            _ => "Adet farki yok."
        };
    }

    private static string CreateAmountNote(decimal? difference)
    {
        return difference switch
        {
            > 0 => "Tutar sube tarafinda fazla gorunuyor.",
            < 0 => "Tutar banka tarafinda fazla gorunuyor.",
            _ => "Tutar farki yok."
        };
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string GetColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string XmlEscape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content.TrimStart());
    }

    private static string CreateContentTypesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;
    }

    private static string CreateRootRelationshipsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;
    }

    private static string CreateWorkbookXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Differences" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
    }

    private static string CreateWorkbookRelationshipsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;
    }
}
