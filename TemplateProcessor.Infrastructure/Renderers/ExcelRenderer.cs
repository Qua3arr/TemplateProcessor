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
                .Select((row, index) => CaptureRow(row, index))
                .ToList();

            var mergeRanges = CaptureMergeRanges(worksheet, rows, startIndex, endIndex);
            var itemKeys = items
                .SelectMany(item => item.Keys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dynamicRowIndexes = GetDynamicRowIndexes(templateRows, mergeRanges, scalars, itemKeys);

            var insertRowNumber = startRowNumber;
            var currentSegment = new List<RowTemplate>();
            bool? currentSegmentIsDynamic = null;

            foreach (var templateRow in templateRows)
            {
                var isDynamic = dynamicRowIndexes.Contains(templateRow.RelativeRowIndex);
                if (currentSegmentIsDynamic.HasValue && currentSegmentIsDynamic.Value != isDynamic)
                {
                    InsertSegment(
                        worksheet,
                        ref insertRowNumber,
                        currentSegment,
                        currentSegmentIsDynamic.Value,
                        mergeRanges,
                        items,
                        scalars);

                    currentSegment.Clear();
                }

                currentSegmentIsDynamic = isDynamic;
                currentSegment.Add(templateRow);
            }

            if (currentSegmentIsDynamic.HasValue)
            {
                InsertSegment(
                    worksheet,
                    ref insertRowNumber,
                    currentSegment,
                    currentSegmentIsDynamic.Value,
                    mergeRanges,
                    items,
                    scalars);
            }

            var insertedRowsCount = insertRowNumber - startRowNumber;
            for (var rowNumber = endRowNumber + insertedRowsCount; rowNumber >= startRowNumber + insertedRowsCount; rowNumber--)
            {
                worksheet.Row(rowNumber).Delete();
            }
        }

        private static IReadOnlyList<MergeTemplate> CaptureMergeRanges(
            IXLWorksheet worksheet,
            IReadOnlyList<IXLRow> rows,
            int startIndex,
            int endIndex)
        {
            if (endIndex <= startIndex + 1)
                return Array.Empty<MergeTemplate>();

            var templateStartRowNumber = rows[startIndex + 1].RowNumber();
            var templateEndRowNumber = rows[endIndex - 1].RowNumber();
            var result = new List<MergeTemplate>();

            foreach (var range in worksheet.MergedRanges.ToList())
            {
                var firstRowNumber = range.FirstRow().RowNumber();
                var lastRowNumber = range.LastRow().RowNumber();

                if (lastRowNumber < templateStartRowNumber || firstRowNumber > templateEndRowNumber)
                    continue;

                if (firstRowNumber < templateStartRowNumber || lastRowNumber > templateEndRowNumber)
                {
                    throw new TemplateParsingException(
                        $"Merged range '{range.RangeAddress}' crosses collection block boundary.");
                }

                result.Add(new MergeTemplate(
                    firstRowNumber - templateStartRowNumber,
                    lastRowNumber - templateStartRowNumber,
                    range.FirstColumn().ColumnNumber(),
                    range.LastColumn().ColumnNumber()));
            }

            return result;
        }

        private static HashSet<int> GetDynamicRowIndexes(
            IReadOnlyList<RowTemplate> rows,
            IReadOnlyList<MergeTemplate> mergeRanges,
            IReadOnlyDictionary<string, object> scalars,
            IReadOnlySet<string> itemKeys)
        {
            var dynamicRowIndexes = rows
                .Where(row => UsesItemData(row, scalars, itemKeys))
                .Select(row => row.RelativeRowIndex)
                .ToHashSet();

            foreach (var mergeRange in mergeRanges)
            {
                var hasDynamicRow = Enumerable
                    .Range(mergeRange.FirstRelativeRowIndex, mergeRange.LastRelativeRowIndex - mergeRange.FirstRelativeRowIndex + 1)
                    .Any(dynamicRowIndexes.Contains);

                if (!hasDynamicRow)
                    continue;

                for (var relativeRowIndex = mergeRange.FirstRelativeRowIndex;
                     relativeRowIndex <= mergeRange.LastRelativeRowIndex;
                     relativeRowIndex++)
                {
                    dynamicRowIndexes.Add(relativeRowIndex);
                }
            }

            return dynamicRowIndexes;
        }

        private static void InsertSegment(
            IXLWorksheet worksheet,
            ref int insertRowNumber,
            IReadOnlyList<RowTemplate> segmentRows,
            bool isDynamic,
            IReadOnlyList<MergeTemplate> allMergeRanges,
            IReadOnlyList<Dictionary<string, object>> items,
            Dictionary<string, object> scalars)
        {
            if (segmentRows.Count == 0)
                return;

            var segmentMergeRanges = GetMergeRangesForSegment(segmentRows, allMergeRanges);
            if (!isDynamic)
            {
                InsertTemplateRows(
                    worksheet,
                    ref insertRowNumber,
                    segmentRows,
                    segmentMergeRanges,
                    scalars,
                    name => new MissingDataException(name));
                return;
            }

            foreach (var item in items)
            {
                InsertTemplateRows(
                    worksheet,
                    ref insertRowNumber,
                    segmentRows,
                    segmentMergeRanges,
                    item,
                    name => new MissingDataException($"Item in collection missing property '{name}'"),
                    scalars);
            }
        }

        private static IReadOnlyList<MergeTemplate> GetMergeRangesForSegment(
            IReadOnlyList<RowTemplate> segmentRows,
            IReadOnlyList<MergeTemplate> allMergeRanges)
        {
            var rowIndexes = segmentRows
                .Select(row => row.RelativeRowIndex)
                .ToHashSet();

            return allMergeRanges
                .Where(range =>
                    rowIndexes.Contains(range.FirstRelativeRowIndex) &&
                    rowIndexes.Contains(range.LastRelativeRowIndex))
                .ToList();
        }

        private static void InsertTemplateRows(
            IXLWorksheet worksheet,
            ref int rowNumber,
            IReadOnlyList<RowTemplate> templates,
            IReadOnlyList<MergeTemplate> mergeRanges,
            IReadOnlyDictionary<string, object> values,
            Func<string, Exception> missingExceptionFactory,
            IReadOnlyDictionary<string, object>? fallbackValues = null)
        {
            if (templates.Count == 0)
                return;

            var firstInsertedRowNumber = rowNumber;
            var firstRelativeRowIndex = templates[0].RelativeRowIndex;

            worksheet.Row(rowNumber).InsertRowsAbove(templates.Count);

            for (var rowOffset = 0; rowOffset < templates.Count; rowOffset++)
            {
                var template = templates[rowOffset];
                var targetRow = worksheet.Row(rowNumber + rowOffset);
                targetRow.Height = template.Height;

                foreach (var cell in template.Cells)
                {
                    var targetCell = targetRow.Cell(cell.ColumnNumber);
                    targetCell.Style = cell.Style;
                    targetCell.Value = ReplacePlaceholders(cell.Text, values, missingExceptionFactory, fallbackValues);
                }
            }

            ApplyMergeRanges(worksheet, firstInsertedRowNumber, firstRelativeRowIndex, mergeRanges);
            rowNumber += templates.Count;
        }

        private static void ApplyMergeRanges(
            IXLWorksheet worksheet,
            int firstInsertedRowNumber,
            int firstRelativeRowIndex,
            IReadOnlyList<MergeTemplate> mergeRanges)
        {
            foreach (var mergeRange in mergeRanges)
            {
                var firstRow = firstInsertedRowNumber + mergeRange.FirstRelativeRowIndex - firstRelativeRowIndex;
                var lastRow = firstInsertedRowNumber + mergeRange.LastRelativeRowIndex - firstRelativeRowIndex;

                if (firstRow == lastRow && mergeRange.FirstColumnNumber == mergeRange.LastColumnNumber)
                    continue;

                worksheet
                    .Range(firstRow, mergeRange.FirstColumnNumber, lastRow, mergeRange.LastColumnNumber)
                    .Merge();
            }
        }

        private static RowTemplate CaptureRow(IXLRow row, int relativeRowIndex)
        {
            var cells = row.CellsUsed()
                .OrderBy(cell => cell.Address.ColumnNumber)
                .Select(cell => new CellTemplate(cell.Address.ColumnNumber, cell.GetString(), cell.Style))
                .ToList();

            return new RowTemplate(relativeRowIndex, row.Height, string.Join("", cells.Select(cell => cell.Text)), cells);
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

        private sealed record RowTemplate(
            int RelativeRowIndex,
            double Height,
            string Text,
            IReadOnlyList<CellTemplate> Cells);

        private sealed record MergeTemplate(
            int FirstRelativeRowIndex,
            int LastRelativeRowIndex,
            int FirstColumnNumber,
            int LastColumnNumber);

        private sealed record CellTemplate(int ColumnNumber, string Text, IXLStyle Style);
    }
}
