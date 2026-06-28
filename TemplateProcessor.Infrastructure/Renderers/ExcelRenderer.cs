using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

//Рендерер для Excel (.xlsx) шаблонов.
namespace TemplateProcessor.Infrastructure.Renderers
{
    public class ExcelRenderer : IDocumentRenderer
    {
        private static readonly Regex PlaceholderRegex = new(
            @"\{\{(?<type>[#\/]?)(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Task<Stream> RenderAsync(
            Stream templateStream,
            TemplateContext context,
            TemplateFormat inputFormat,
            OutputFormat outputFormat,
            CancellationToken cancellationToken = default)
        {
            if (inputFormat != TemplateFormat.Excel)
                throw new ArgumentException("Renderer supports only Excel format", nameof(inputFormat));

            if (outputFormat != OutputFormat.Xlsx)
                throw new UnsupportedFormatException($"ExcelRenderer does not support output format: {outputFormat}");

            var memoryStream = new MemoryStream();
            templateStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var workbook = new XLWorkbook(memoryStream);
            foreach (var worksheet in workbook.Worksheets)
            {
                ReplaceScalars(worksheet, context.Scalars);
                ProcessCollections(worksheet, context.Collections);
            }

            workbook.SaveAs(memoryStream);
            memoryStream.Position = 0;
            return Task.FromResult<Stream>(memoryStream);
        }

        private static void ReplaceScalars(IXLWorksheet worksheet, Dictionary<string, object> scalars)
        {
            var cells = worksheet.CellsUsed(c => !string.IsNullOrEmpty(c.GetString()));
            foreach (var cell in cells)
            {
                var text = cell.GetString();
                if (string.IsNullOrEmpty(text))
                    continue;

                var matches = PlaceholderRegex.Matches(text);
                if (matches.Count == 0)
                    continue;

                var newText = text;
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value.Trim();
                    var type = match.Groups["type"].Value;

                    if (type == "#" || type == "/")
                        continue;

                    if (scalars.TryGetValue(name, out var value))
                    {
                        newText = newText.Replace(match.Value, value?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        throw new MissingDataException(name);
                    }
                }
                cell.Value = newText;
            }
        }

        private static void ProcessCollections(IXLWorksheet worksheet, Dictionary<string, IEnumerable<Dictionary<string, object>>> collections)
        {
            var usedRows = worksheet.RowsUsed();
            if (usedRows == null || !usedRows.Any())
                return;

            var rows = usedRows.ToList();
            var rowsToRemove = new List<IXLRow>();
            var newRows = new List<IXLRow>();

            foreach (var row in rows)
            {
                var rowText = GetRowText(row);
                var cleanRowText = Regex.Replace(rowText, @"\s+", "");

                var matchStart = Regex.Match(cleanRowText, @"\{\{#(?<name>[^{}]+)\}\}");
                if (matchStart.Success)
                {
                    var collectionName = matchStart.Groups["name"].Value.Trim();
                    if (!collections.TryGetValue(collectionName, out var items))
                        throw new MissingDataException(collectionName);

                    foreach (var item in items)
                    {
                        var clonedRow = CloneRow(row, item);
                        newRows.Add(clonedRow);
                    }

                    rowsToRemove.Add(row);
                    continue;
                }

                var matchEnd = Regex.Match(cleanRowText, @"\{\{/(?<name>[^{}]+)\}\}");
                if (matchEnd.Success)
                {
                    rowsToRemove.Add(row);
                    continue;
                }
            }

            if (newRows.Count > 0 && rowsToRemove.Count > 0)
            {
                var firstRemoved = rowsToRemove.First();
                var insertIndex = firstRemoved.RowNumber();
                foreach (var newRow in newRows)
                {
                    worksheet.Row(insertIndex).InsertRowsAbove(1);
                    var targetRow = worksheet.Row(insertIndex);
                    CopyRowData(newRow, targetRow);
                    insertIndex++;
                }
            }

            foreach (var row in rowsToRemove.OrderByDescending(r => r.RowNumber()))
            {
                row.Delete();
            }
        }

        private static string GetRowText(IXLRow row)
        {
            return string.Join("", row.Cells().Select(c => c.GetString()));
        }

        private static IXLRow CloneRow(IXLRow original, Dictionary<string, object> itemData)
        {
            var worksheet = original.Worksheet;
            var newRow = worksheet.Row(original.RowNumber()).InsertRowsBelow(1).First();

            CopyRowData(original, newRow);

            foreach (var cell in newRow.Cells())
            {
                var text = cell.GetString();
                if (string.IsNullOrEmpty(text))
                    continue;

                var newText = text;
                var matches = PlaceholderRegex.Matches(text);
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value.Trim();
                    var type = match.Groups["type"].Value;

                    if (type == "#" || type == "/")
                        continue;

                    if (itemData.TryGetValue(name, out var value))
                    {
                        newText = newText.Replace(match.Value, value?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        throw new MissingDataException($"Item in collection missing property '{name}'");
                    }
                }
                cell.Value = newText;
            }

            return newRow;
        }

        private static void CopyRowData(IXLRow source, IXLRow target)
        {
            var cells = source.Cells().ToList();
            for (int i = 0; i < cells.Count; i++)
            {
                var sourceCell = cells[i];
                var targetCell = target.Cell(i + 1);
                targetCell.Value = sourceCell.GetString();
            }
        }
    }
}
