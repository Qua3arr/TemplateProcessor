using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Infrastructure.Analyzers
{
    public class LatexTemplateAnalyzer : ITemplateAnalyzer
    {
        private static readonly Regex PlaceholderRegex = new(
            @"\{\{(?<type>[#\/]?)(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public Task<IReadOnlyList<TemplateVariable>> AnalyzeAsync(
            Stream templateStream,
            TemplateFormat format,
            CancellationToken cancellationToken = default)
        {
            if (format != TemplateFormat.Latex)
                throw new ArgumentException("Format must be LaTeX", nameof(format));

            var variables = new HashSet<TemplateVariable>(new TemplateVariableComparer());

            using var reader = new StreamReader(templateStream);
            var content = reader.ReadToEnd();

            //Ищем маркеры коллекций и скаляры
            var insideCollection = false;

            foreach (Match match in PlaceholderRegex.Matches(content))
            {
                var name = match.Groups["name"].Value.Trim();
                var typeChar = match.Groups["type"].Value;

                if (string.IsNullOrEmpty(name))
                    continue;

                if (typeChar == "#")
                {
                    insideCollection = true;
                    variables.Add(new TemplateVariable(name, VariableType.Collection));
                }
                else if (typeChar == "/")
                {
                    insideCollection = false;
                }
                else
                {
                    if (!insideCollection)
                    {
                        variables.Add(new TemplateVariable(name, VariableType.Scalar));
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<TemplateVariable>>(variables.ToList());
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
