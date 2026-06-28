using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

//Рендерер для Ворд шаблонов
namespace TemplateProcessor.Infrastructure.Renderers
{
    public class WordRenderer : IDocumentRenderer
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
            if (inputFormat != TemplateFormat.Word)
                throw new ArgumentException("Renderer supports only Word format", nameof(inputFormat));

            if (outputFormat != OutputFormat.Docx)
                throw new UnsupportedFormatException($"WordRenderer does not support output format: {outputFormat}");

            var memoryStream = new MemoryStream();
            templateStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var document = WordprocessingDocument.Open(memoryStream, true);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
                throw new TemplateParsingException("Document body is empty or not found.");

            ProcessCollections(body, context.Collections, context.Scalars);
            ReplaceScalars(body, context.Scalars);

            document.Save();
            memoryStream.Position = 0;
            return Task.FromResult<Stream>(memoryStream);
        }

        private static void ReplaceScalars(OpenXmlElement container, Dictionary<string, object> scalars)
        {
            ReplacePlaceholders(container, scalars, name => new MissingDataException(name));
        }

        private static void ProcessCollections(
            OpenXmlElement container,
            Dictionary<string, IEnumerable<Dictionary<string, object>>> collections,
            Dictionary<string, object> scalars)
        {
            var tables = container.Descendants<Table>().ToList();
            foreach (var table in tables)
            {
                ProcessTable(table, collections, scalars);
            }
        }

        private static void ProcessTable(
            Table table,
            Dictionary<string, IEnumerable<Dictionary<string, object>>> collections,
            Dictionary<string, object> scalars)
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count == 0)
                return;

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

                var templateRows = rows.Skip(i + 1).Take(endIndex - i - 1).ToList();

                foreach (var item in items)
                {
                    foreach (var templateRow in templateRows)
                    {
                        var clonedRow = CloneRow(templateRow, item, scalars);
                        table.InsertBefore(clonedRow, row);
                    }
                }

                for (var removeIndex = i; removeIndex <= endIndex; removeIndex++)
                {
                    rows[removeIndex].Remove();
                }

                i = endIndex;
            }
        }

        private static int FindCollectionEndIndex(IReadOnlyList<TableRow> rows, int startIndex, string collectionName)
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

        private static bool TryGetCollectionName(TableRow row, Regex markerRegex, out string collectionName)
        {
            var rowText = GetElementText(row);
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

        private static TableRow CloneRow(
            TableRow original,
            Dictionary<string, object> itemData,
            Dictionary<string, object> scalars)
        {
            var newRow = original.CloneNode(true) as TableRow;
            if (newRow == null)
                throw new InvalidOperationException("Failed to clone row");

            ReplacePlaceholders(
                newRow,
                itemData,
                name => new MissingDataException($"Item in collection missing property '{name}'"),
                scalars);

            return newRow;
        }

        private static void ReplacePlaceholders(
            OpenXmlElement container,
            IReadOnlyDictionary<string, object> values,
            Func<string, Exception> missingExceptionFactory,
            IReadOnlyDictionary<string, object>? fallbackValues = null)
        {
            var paragraphs = container is Paragraph paragraph
                ? new[] { paragraph }
                : container.Descendants<Paragraph>().ToArray();

            foreach (var paragraphToProcess in paragraphs)
            {
                ReplacePlaceholdersInParagraph(paragraphToProcess, values, missingExceptionFactory, fallbackValues);
            }
        }

        private static void ReplacePlaceholdersInParagraph(
            Paragraph paragraph,
            IReadOnlyDictionary<string, object> values,
            Func<string, Exception> missingExceptionFactory,
            IReadOnlyDictionary<string, object>? fallbackValues)
        {
            var textElements = paragraph.Descendants<Text>().ToList();
            if (textElements.Count == 0)
                return;

            var text = string.Join("", textElements.Select(t => t.Text));
            if (string.IsNullOrEmpty(text) || !PlaceholderRegex.IsMatch(text))
                return;

            var newText = PlaceholderRegex.Replace(text, match =>
            {
                var name = match.Groups["name"].Value.Trim();
                var type = match.Groups["type"].Value;

                if (type == "#" || type == "/")
                    return match.Value;

                if (TryGetReplacement(values, name, out var replacement))
                    return replacement;

                if (fallbackValues != null && TryGetReplacement(fallbackValues, name, out replacement))
                    return replacement;

                throw missingExceptionFactory(name);
            });

            textElements[0].Text = newText;
            textElements[0].Space = SpaceProcessingModeValues.Preserve;

            foreach (var textElement in textElements.Skip(1))
            {
                textElement.Text = string.Empty;
            }
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

        private static string GetElementText(OpenXmlElement element)
        {
            return string.Join("", element.Descendants<Text>().Select(t => t.Text));
        }
    }
}
