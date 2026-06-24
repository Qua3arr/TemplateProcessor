using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Application.UseCases
{
    public class GetRequiredVariablesUseCase
    {
        private readonly ITemplateStorage _storage;
        private readonly ITemplateAnalyzer _analyzer;

        public GetRequiredVariablesUseCase(ITemplateStorage storage, ITemplateAnalyzer analyzer)
        {
            _storage = storage;
            _analyzer = analyzer;
        }

        //Выполняет анализ шаблона.
        public async Task<IReadOnlyList<TemplateVariable>> ExecuteAsync(
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            var format = GetFormatFromPath(templatePath);

            await using var stream = await _storage.ReadAsync(templatePath, cancellationToken);

            return await _analyzer.AnalyzeAsync(stream, format, cancellationToken);
        }

        //Определяет формат шаблона по расширению файла.
        private static TemplateFormat GetFormatFromPath(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".docx" => TemplateFormat.Word,
                ".xlsx" => TemplateFormat.Excel,
                ".tex" => TemplateFormat.Latex,
                _ => throw new Domain.Exceptions.UnsupportedFormatException(
                    $"Unsupported template format: '{extension}'. Supported: .docx, .xlsx, .tex")
            };
        }
    }
}
