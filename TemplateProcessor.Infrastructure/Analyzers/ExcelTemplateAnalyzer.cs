using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Infrastructure.Analyzers
{
    public class ExcelTemplateAnalyzer : ITemplateAnalyzer
    {
        private static readonly Regex PlaceholderRegex = new(
            @"\{\{(?<type>[#\/]?)(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Task<IReadOnlyList<TemplateVariable>> AnalyzeAsync(
            Stream templateStream,
            TemplateFormat format,
            CancellationToken cancellationToken = default)
        {
            if (format != TemplateFormat.Excel)
                throw new ArgumentException("Format must be Excel", nameof(format));

            var variables = new HashSet<TemplateVariable>(new TemplateVariableComparer());
            var insideCollection = false;

            using var workbook = new XLWorkbook(templateStream);
            //Проходим по всем листам
            foreach (var worksheet in workbook.Worksheets)
            {
                //Проходим по всем ячейкам, в которых есть текст
                var cells = worksheet.CellsUsed(c => !string.IsNullOrEmpty(c.GetString()));
                foreach (var cell in cells)
                {
                    var text = cell.GetString();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    //Проверяем маркеры начала и конца коллекции
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

                    //Если мы внутри коллекции, не добавляем переменные из этой ячейки
                    if (insideCollection)
                        continue;

                    //Ищем обычные плейсхолдеры (скаляры) вне коллекций
                    FindPlaceholders(text, variables);
                }
            }

            return Task.FromResult<IReadOnlyList<TemplateVariable>>(variables.ToList());
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
