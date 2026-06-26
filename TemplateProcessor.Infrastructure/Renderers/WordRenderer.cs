using System;
using System.Collections.Generic;
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
            var body = document.MainDocumentPart?.Document.Body;
            if (body == null)
                throw new TemplateParsingException("Document body is empty or not found.");

            ReplaceScalars(body, context.Scalars);
            ProcessCollections(body, context.Collections);

            document.Save();
            memoryStream.Position = 0;
            return Task.FromResult<Stream>(memoryStream);
        }

        private static void ReplaceScalars(OpenXmlElement container, Dictionary<string, object> scalars)
        {
            var runs = container.Descendants<Run>().ToList();
            foreach (var run in runs)
            {
                var text = run.GetFirstChild<Text>();
                if (text == null || string.IsNullOrEmpty(text.Text))
                    continue;

                var matches = PlaceholderRegex.Matches(text.Text);
                if (matches.Count == 0)
                    continue;

                var newText = text.Text;
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value.Trim();
                    var type = match.Groups["type"].Value;

                    if (type == "#" || type == "/")
                        continue;

                    if (scalars.TryGetValue(name, out var value))
                    {
                        var replacement = value?.ToString() ?? string.Empty;
                        newText = newText.Replace(match.Value, replacement);
                    }
                    else
                    {
                        throw new MissingDataException(name);
                    }
                }
                text.Text = newText;
            }
        }

        private static void ProcessCollections(OpenXmlElement container, Dictionary<string, IEnumerable<Dictionary<string, object>>> collections)
        {
            var tables = container.Descendants<Table>().ToList();
            foreach (var table in tables)
            {
                ProcessTable(table, collections);
            }
        }

        private static void ProcessTable(Table table, Dictionary<string, IEnumerable<Dictionary<string, object>>> collections)
        {
            var rows = table.Descendants<TableRow>().ToList();
            if (rows.Count == 0)
                return;

            var rowsToRemove = new List<TableRow>();
            var newRows = new List<TableRow>();

            foreach (var row in rows)
            {
                var rowText = GetRowText(row);

                var matchStart = Regex.Match(rowText, @"\{\{#(?<name>[^{}]+)\}\}");
                if (matchStart.Success)
                {
                    var collectionName = matchStart.Groups["name"].Value.Trim();

                    if (!collections.TryGetValue(collectionName, out var items))
                    {
                        throw new MissingDataException(collectionName);
                    }

                    foreach (var item in items)
                    {
                        var clonedRow = CloneRow(row, item);
                        newRows.Add(clonedRow);
                    }

                    rowsToRemove.Add(row);
                    continue;
                }

                var matchEnd = Regex.Match(rowText, @"\{\{/(?<name>[^{}]+)\}\}");
                if (matchEnd.Success)
                {
                    rowsToRemove.Add(row);
                    continue;
                }
            }

            //Если есть новые строки, вставляем их перед первой удаляемой
            if (newRows.Count > 0 && rowsToRemove.Count > 0)
            {
                var firstRemoved = rowsToRemove.First();
                foreach (var newRow in newRows)
                {
                    table.InsertBefore(newRow, firstRemoved);
                }
            }

            //Удаляем маркерные строки
            foreach (var row in rowsToRemove)
            {
                row.Remove();
            }
        }

        private static string GetRowText(TableRow row)
        {
            return string.Join("", row.Descendants<Text>().Select(t => t.Text));
        }

        private static TableRow CloneRow(TableRow original, Dictionary<string, object> itemData)
        {
            var newRow = original.CloneNode(true) as TableRow;
            if (newRow == null)
                throw new InvalidOperationException("Failed to clone row");

            var textElements = newRow.Descendants<Text>().ToList();
            foreach (var text in textElements)
            {
                if (string.IsNullOrEmpty(text.Text))
                    continue;

                var newText = text.Text;
                var matches = PlaceholderRegex.Matches(text.Text);
                foreach (Match match in matches)
                {
                    var name = match.Groups["name"].Value.Trim();
                    var type = match.Groups["type"].Value;

                    if (type == "#" || type == "/")
                        continue;

                    if (itemData.TryGetValue(name, out var value))
                    {
                        var replacement = value?.ToString() ?? string.Empty;
                        newText = newText.Replace(match.Value, replacement);
                    }
                    else
                    {
                        throw new MissingDataException($"Item in collection missing property '{name}'");
                    }
                }
                text.Text = newText;
            }

            return newRow;
        }
    }
}
