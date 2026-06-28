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

        public Task<IReadOnlyList<TemplateVariable>> AnalyzeAsync(
            Stream templateStream,
            TemplateFormat format,
            CancellationToken cancellationToken = default)
        {
            if (format != TemplateFormat.Word)
                throw new ArgumentException("Format must be Word", nameof(format));

            var variables = new HashSet<TemplateVariable>(new TemplateVariableComparer());
            var insideCollection = false;

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

                //Проверяем, есть ли маркер начала или конца коллекции
                var matchStart = Regex.Match(text, @"\{\{#(?<name>[^{}]+)\}\}");
                var matchEnd = Regex.Match(text, @"\{\{/(?<name>[^{}]+)\}\}");

                if (matchStart.Success)
                {
                    var name = matchStart.Groups["name"].Value.Trim();
                    variables.Add(new TemplateVariable(name, VariableType.Collection));
                    insideCollection = true;
                    continue; 
                }

                if (matchEnd.Success)
                {
                    insideCollection = false;
                    continue;
                }

                //Если мы внутри коллекции, не добавляем переменные из этого параграфа
                if (insideCollection)
                    continue;

                //Ищем обычные плейсхолдеры (скаляры) только вне коллекций
                FindPlaceholders(text, variables);
            }

            return Task.FromResult<IReadOnlyList<TemplateVariable>>(variables.ToList());
        }

        private static string ExtractTextFromParagraph(Paragraph paragraph)
        {
            var runs = paragraph.Descendants<Run>().ToList();
            return runs.Count == 0 ? string.Empty : string.Join("", runs.Select(r => r.InnerText)).Trim();
        }

        private static void FindPlaceholders(string text, HashSet<TemplateVariable> variables)
        {
            var matches = PlaceholderRegex.Matches(text);
            foreach (Match match in matches)
            {
                var typeChar = match.Groups["type"].Value;
                var name = match.Groups["name"].Value.Trim();

                if (string.IsNullOrEmpty(name))
                    continue;

                //Игнорируем маркеры коллекций (их мы уже обработали отдельно)
                if (typeChar is "#" or "/")
                    continue;

                variables.Add(new TemplateVariable(name, VariableType.Scalar));
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
