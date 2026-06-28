using System;
using System.Collections.Generic;
using System.Globalization;
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

        private static readonly Regex CollectionStartRegex = new(
            @"\{\{#(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CollectionEndRegex = new(
            @"\{\{/(?<name>[^{}]+)\}\}",
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
                ProcessCollections(worksheet, context.Collections, context.Scalars);
                ReplaceScalars(worksheet, context.Scalars);
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
                    var type = match.Groups["type"].Value;
                    if (type == "#" || type == "/")
                        continue;

                    var name = match.Groups["name"].Value.Trim();
                    if (!TryGetReplacement(scalars, name, out var replacement))
                        throw new MissingDataException(name);

                    newText = newText.Replace(match.Value, replacement);
                }

                cell.Value = newText;
            }
        }

        private static void ProcessCollections(
            IXLWorksheet worksheet,
            Dictionary<string, IEnumerable<Dictionary<string, object>>> collections,
            Dictionary<string, object> scalars)
        {
            while (ProcessNextCollection(worksheet, collections, scalars))
            {
            }
        }

        private static bool ProcessNextCollection(
            IXLWorksheet worksheet,
            Dictionary<string, IEnumerable<Dictionary<string, object>>> collections,
            Dictionary<string, object> scalars)
        {
            var rows = worksheet.RowsUsed().OrderBy(row => row.RowNumber()).ToList();
            if (rows.Count == 0)
                return false;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                if (!TryGetCollectionName(row, CollectionStartRegex, out var collectionName))
                {
                    if (TryGetCollectionName(row, CollectionEndRegex, out var unexpectedCollectionName))
                        throw new TemplateParsingException($"Collection end marker '{{{{/{unexpectedCollectionName}}}}}' has no matching start marker.");

                    continue;
                }

                if (!collections.TryGetValue(collectionName, out var items))
                    throw new MissingDataException(collectionName);

                var endIndex = FindCollectionEndIndex(rows, i + 1, collectionName);
                if (endIndex < 0)
                    throw new TemplateParsingException($"Missing end marker for collection '{collectionName}'.");

                RenderCollectionBlock(worksheet, rows, i, endIndex, items.ToList(), scalars);
                return true;
            }

            return false;
        }

        private static int FindCollectionEndIndex(IReadOnlyList<IXLRow> rows, int startIndex, string collectionName)
        {
            var depth = 0;

            for (var i = startIndex; i < rows.Count; i++)
            {
                if (TryGetCollectionName(rows[i], CollectionStartRegex, out var nestedCollectionName) &&
                    string.Equals(nestedCollectionName, collectionName, StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                    continue;
                }

                if (TryGetCollectionName(rows[i], CollectionEndRegex, out var endCollectionName) &&
                    string.Equals(endCollectionName, collectionName, StringComparison.OrdinalIgnoreCase))
                {
                    if (depth == 0)
                        return i;

                    depth--;
                }
            }

            return -1;
        }

        private static void RenderCollectionBlock(
            IXLWorksheet worksheet,
            IReadOnlyList<IXLRow> rows,
            int startIndex,
            int endIndex,
            IReadOnlyList<Dictionary<string, object>> items,
            Dictionary<string, object> scalars)
        {
            var startRowNumber = rows[startIndex].RowNumber();
            var endRowNumber = rows[endIndex].RowNumber();
            var templateRows = rows
                .Skip(startIndex + 1)
                .Take(endIndex - startIndex - 1)
                .Select(CaptureRow)
                .ToList();

            var itemKeys = items
                .SelectMany(item => item.Keys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var insertRowNumber = startRowNumber;
            var dynamicRows = new List<RowTemplate>();

            foreach (var templateRow in templateRows)
            {
                if (UsesItemData(templateRow, scalars, itemKeys))
                {
                    dynamicRows.Add(templateRow);
                    continue;
                }

                InsertDynamicRows(worksheet, ref insertRowNumber, dynamicRows, items, scalars);
                dynamicRows.Clear();

                InsertTemplateRow(
                    worksheet,
                    ref insertRowNumber,
                    templateRow,
                    scalars,
                    name => new MissingDataException(name));
            }

            InsertDynamicRows(worksheet, ref insertRowNumber, dynamicRows, items, scalars);

            var insertedRowsCount = insertRowNumber - startRowNumber;
            for (var rowNumber = endRowNumber + insertedRowsCount; rowNumber >= startRowNumber + insertedRowsCount; rowNumber--)
            {
                worksheet.Row(rowNumber).Delete();
            }
        }

        private static void InsertDynamicRows(
            IXLWorksheet worksheet,
            ref int insertRowNumber,
            IReadOnlyList<RowTemplate> dynamicRows,
            IReadOnlyList<Dictionary<string, object>> items,
            Dictionary<string, object> scalars)
        {
            if (dynamicRows.Count == 0)
                return;

            foreach (var item in items)
            {
                foreach (var templateRow in dynamicRows)
                {
                    InsertTemplateRow(
                        worksheet,
                        ref insertRowNumber,
                        templateRow,
                        item,
                        name => new MissingDataException($"Item in collection missing property '{name}'"),
                        scalars);
                }
            }
        }

        private static void InsertTemplateRow(
            IXLWorksheet worksheet,
            ref int rowNumber,
            RowTemplate template,
            IReadOnlyDictionary<string, object> values,
            Func<string, Exception> missingExceptionFactory,
            IReadOnlyDictionary<string, object>? fallbackValues = null)
        {
            worksheet.Row(rowNumber).InsertRowsAbove(1);
            var targetRow = worksheet.Row(rowNumber);
            targetRow.Height = template.Height;

            foreach (var cell in template.Cells)
            {
                var targetCell = targetRow.Cell(cell.ColumnNumber);
                targetCell.Style = cell.Style;
                targetCell.Value = ReplacePlaceholders(cell.Text, values, missingExceptionFactory, fallbackValues);
            }

            rowNumber++;
        }

        private static RowTemplate CaptureRow(IXLRow row)
        {
            var cells = row.CellsUsed()
                .OrderBy(cell => cell.Address.ColumnNumber)
                .Select(cell => new CellTemplate(cell.Address.ColumnNumber, cell.GetString(), cell.Style))
                .ToList();

            return new RowTemplate(row.Height, string.Join("", cells.Select(cell => cell.Text)), cells);
        }

        private static bool UsesItemData(
            RowTemplate template,
            IReadOnlyDictionary<string, object> scalars,
            IReadOnlySet<string> itemKeys)
        {
            var matches = PlaceholderRegex.Matches(template.Text);
            foreach (Match match in matches)
            {
                var type = match.Groups["type"].Value;
                if (type == "#" || type == "/")
                    continue;

                var name = match.Groups["name"].Value.Trim();
                if (itemKeys.Contains(name) || !scalars.ContainsKey(name))
                    return true;
            }

            return false;
        }

        private static string ReplacePlaceholders(
            string text,
            IReadOnlyDictionary<string, object> values,
            Func<string, Exception> missingExceptionFactory,
            IReadOnlyDictionary<string, object>? fallbackValues = null)
        {
            if (string.IsNullOrEmpty(text) || !PlaceholderRegex.IsMatch(text))
                return text;

            return PlaceholderRegex.Replace(text, match =>
            {
                var type = match.Groups["type"].Value;
                if (type == "#" || type == "/")
                    return match.Value;

                var name = match.Groups["name"].Value.Trim();
                if (TryGetReplacement(values, name, out var replacement))
                    return replacement;

                if (fallbackValues != null && TryGetReplacement(fallbackValues, name, out replacement))
                    return replacement;

                throw missingExceptionFactory(name);
            });
        }

        private static bool TryGetCollectionName(IXLRow row, Regex markerRegex, out string collectionName)
        {
            var rowText = GetRowText(row);
            var normalizedText = Regex.Replace(rowText, @"\s+", string.Empty);
            var match = markerRegex.Match(normalizedText);

            if (match.Success)
            {
                collectionName = match.Groups["name"].Value.Trim();
                return true;
            }

            collectionName = string.Empty;
            return false;
        }

        private static string GetRowText(IXLRow row)
        {
            return string.Join("", row.CellsUsed().Select(c => c.GetString()));
        }

        private static bool TryGetReplacement(
            IReadOnlyDictionary<string, object> values,
            string name,
            out string replacement)
        {
            if (values.TryGetValue(name, out var value))
            {
                replacement = FormatValue(value);
                return true;
            }

            replacement = string.Empty;
            return false;
        }

        private static string FormatValue(object value)
        {
            return value switch
            {
                null => string.Empty,
                decimal decimalValue => decimalValue.ToString("0.00", CultureInfo.InvariantCulture),
                double doubleValue => doubleValue.ToString("0.00", CultureInfo.InvariantCulture),
                float floatValue => floatValue.ToString("0.00", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private sealed record RowTemplate(double Height, string Text, IReadOnlyList<CellTemplate> Cells);

        private sealed record CellTemplate(int ColumnNumber, string Text, IXLStyle Style);
    }
}
