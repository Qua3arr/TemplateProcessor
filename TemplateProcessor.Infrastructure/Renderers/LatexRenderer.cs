using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Converters;

//Рендерер для Latex
namespace TemplateProcessor.Infrastructure.Renderers
{
    public class LatexRenderer : IDocumentRenderer
    {
        private static readonly Regex PlaceholderRegex = new(
            @"\{\{(?<type>[#\/]?)(?<name>[^{}]+)\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CollectionPattern = new(
            @"\{\{#(?<name>[^{}]+)\}\}(?<template>.*?)\{\{/\k<name>\}\}",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly Lazy<LatexToPdfConverter> _pdfConverter;

        public LatexRenderer() : this(() => new LatexToPdfConverter()) { }

        public LatexRenderer(LatexToPdfConverter pdfConverter)
        {
            if (pdfConverter == null)
                throw new ArgumentNullException(nameof(pdfConverter));

            _pdfConverter = new Lazy<LatexToPdfConverter>(() => pdfConverter);
        }

        private LatexRenderer(Func<LatexToPdfConverter> pdfConverterFactory)
        {
            _pdfConverter = new Lazy<LatexToPdfConverter>(
                pdfConverterFactory ?? throw new ArgumentNullException(nameof(pdfConverterFactory)));
        }

        public async Task<Stream> RenderAsync(
            Stream templateStream,
            TemplateContext context,
            TemplateFormat inputFormat,
            OutputFormat outputFormat,
            CancellationToken cancellationToken = default)
        {
            if (inputFormat != TemplateFormat.Latex)
                throw new ArgumentException("Renderer supports only LaTeX format", nameof(inputFormat));

            //Генерируем .tex
            var texStream = RenderToTex(templateStream, context);

            if (outputFormat == OutputFormat.Pdf)
            {
                return await _pdfConverter.Value.ConvertAsync(texStream, cancellationToken);
            }

            if (outputFormat == OutputFormat.Tex)
            {
                return texStream;
            }

            throw new UnsupportedFormatException($"LatexRenderer does not support output format: {outputFormat}");
        }

        private Stream RenderToTex(Stream templateStream, TemplateContext context)
        {
            using var reader = new StreamReader(templateStream);
            var content = reader.ReadToEnd();

            //Сначала обрабатываем коллекции
            content = ProcessCollections(content, context.Collections);

            //Затем скаляры
            content = ReplaceScalars(content, context.Scalars);

            var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true);
            writer.Write(content);
            writer.Flush();
            memoryStream.Position = 0;
            return memoryStream;
        }

        private static string ReplaceScalars(string content, Dictionary<string, object> scalars)
        {
            return ReplacePlaceholders(content, name =>
            {
                if (!scalars.TryGetValue(name, out var value))
                    throw new MissingDataException(name);

                return EscapeLaTeX(value?.ToString() ?? string.Empty);
            });
        }

        private static string ProcessCollections(string content, Dictionary<string, IEnumerable<Dictionary<string, object>>> collections)
        {
            return CollectionPattern.Replace(content, match =>
            {
                var collectionName = match.Groups["name"].Value.Trim();
                var template = match.Groups["template"].Value;

                if (!collections.TryGetValue(collectionName, out var items))
                    throw new MissingDataException(collectionName);

                var itemList = items as IReadOnlyCollection<Dictionary<string, object>> ?? items.ToList();
                var result = new StringBuilder(template.Length * Math.Max(itemList.Count, 1));
                foreach (var item in itemList)
                {
                    var row = ReplacePlaceholders(template, name =>
                    {
                        if (!item.TryGetValue(name, out var value))
                            throw new MissingDataException($"Item in collection missing property '{name}'");

                        return EscapeLaTeX(value?.ToString() ?? string.Empty);
                    });

                    result.Append(row);
                }

                return result.ToString();
            });
        }

        private static string ReplacePlaceholders(string text, Func<string, string> replacementFactory)
        {
            if (string.IsNullOrEmpty(text) || !PlaceholderRegex.IsMatch(text))
                return text;

            return PlaceholderRegex.Replace(text, match =>
            {
                var type = match.Groups["type"].Value;
                if (type == "#" || type == "/")
                    return match.Value;

                var name = match.Groups["name"].Value.Trim();
                return replacementFactory(name);
            });
        }

        private static string EscapeLaTeX(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                sb.Append(ch switch
                {
                    '\\' => "\\textbackslash{}",
                    '#' => "\\#",
                    '$' => "\\$",
                    '%' => "\\%",
                    '&' => "\\&",
                    '_' => "\\_",
                    '{' => "\\{",
                    '}' => "\\}",
                    '~' => "\\textasciitilde{}",
                    '^' => "\\textasciicircum{}",
                    _ => ch.ToString()
                });
            }

            return sb.ToString();
        }
    }
}
