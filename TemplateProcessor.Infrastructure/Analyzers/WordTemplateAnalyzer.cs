using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Infrastructure.Analyzers
{
    public class WordTemplateAnalyzer : ITemplateAnalyzer
    {
        private static readonly Regex PlaceholderRegex = new(
            @"\{\{(?<type>[#\/]?)(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


        //Анализирует .docx шаблон и извлекает все переменные
        public Task<IReadOnlyList<TemplateVariable>> AnalyzeAsync(
            Stream templateStream,
            TemplateFormat format,
            CancellationToken cancellationToken = default)
        {
            if (format != TemplateFormat.Word)
                throw new ArgumentException("Format must be Word", nameof(format));

            var variables = new HashSet<TemplateVariable>(new TemplateVariableComparer());

            using var document = WordprocessingDocument.Open(templateStream, false);
            var body = document.MainDocumentPart?.Document.Body;
            if (body == null)
                return Task.FromResult<IReadOnlyList<TemplateVariable>>(variables.ToList());

            //Проходим по всем параграфам
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                var text = ExtractTextFromParagraph(paragraph);
                if (string.IsNullOrEmpty(text))
                    continue;

                FindPlaceholders(text, variables);
            }

            //Также обрабатываем таблицы (если есть)
            foreach (var table in body.Descendants<Table>())
            {
                foreach (var row in table.Descendants<TableRow>())
                {
                    foreach (var cell in row.Descendants<TableCell>())
                    {
                        foreach (var paragraph in cell.Descendants<Paragraph>())
                        {
                            var text = ExtractTextFromParagraph(paragraph);
                            if (string.IsNullOrEmpty(text))
                                continue;

                            FindPlaceholders(text, variables);
                        }
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<TemplateVariable>>(variables.ToList());
        }

        //извлекает текст из параграфа
        private static string ExtractTextFromParagraph(Paragraph paragraph)
        {
            var runs = paragraph.Descendants<Run>().ToList();
            if (runs.Count == 0)
                return string.Empty;

            var text = string.Join("", runs.Select(r => r.InnerText));
            return text.Trim();
        }

        //Ищет плейсхолдеры в тексте и добавляет их в коллекцию
        private static void FindPlaceholders(string text, HashSet<TemplateVariable> variables)
        {
            var matches = PlaceholderRegex.Matches(text);
            foreach (Match match in matches)
            {
                var typeChar = match.Groups["type"].Value;
                var name = match.Groups["name"].Value.Trim();

                if (string.IsNullOrEmpty(name))
                    continue;

                var variableType = typeChar switch
                {
                    "#" => VariableType.Collection,
                    "/" => VariableType.Collection,
                    _ => VariableType.Scalar
                };

                variables.Add(new TemplateVariable(name, variableType));
            }
        }

        private class TemplateVariableComparer : IEqualityComparer<TemplateVariable>
        {
            public bool Equals(TemplateVariable x, TemplateVariable y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(TemplateVariable obj)
            {
                return obj.Name?.ToLowerInvariant().GetHashCode() ?? 0;
            }
        }
    }
}
