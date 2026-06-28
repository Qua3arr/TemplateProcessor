using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.Services;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Domain.Exceptions;

//генерация документа из шаблона и данных.
namespace TemplateProcessor.Application.UseCases
{
    public class RenderDocumentUseCase
    {
        private readonly ITemplateStorage _storage;
        private readonly ITemplateAnalyzer _analyzer;
        private readonly IDocumentRenderer _renderer;

        public RenderDocumentUseCase(
            ITemplateStorage storage,
            ITemplateAnalyzer analyzer,
            IDocumentRenderer renderer)
        {
            _storage = storage;
            _analyzer = analyzer;
            _renderer = renderer;
        }

        //Выполняет генерацию документа.
        public async Task<Stream> ExecuteAsync(
        string templatePath,
        OutputFormat outputFormat,
        TemplateContext context,
        CancellationToken cancellationToken = default)
        {
            try
            {
                var inputFormat = GetFormatFromPath(templatePath);

                await using var templateStream = await _storage.ReadAsync(templatePath, cancellationToken);

                var variables = await _analyzer.AnalyzeAsync(templateStream, inputFormat, cancellationToken);

                TemplateValidationService.Validate(variables, context);

                templateStream.Seek(0, SeekOrigin.Begin);

                var resultStream = await _renderer.RenderAsync(
                    templateStream,
                    context,
                    inputFormat,
                    outputFormat,
                    cancellationToken);

                return resultStream;
            }
            catch (MissingDataException)
            {
                throw;
            }
            catch (UnsupportedFormatException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not (MissingDataException or UnsupportedFormatException))
            {
                throw new TemplateParsingException($"Error during document rendering: {ex.Message}", ex);
            }
        }

        //Определяет формат шаблона по расширению файла.
        private static TemplateFormat GetFormatFromPath(string path)
        {
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".docx" => TemplateFormat.Word,
                ".xlsx" => TemplateFormat.Excel,
                ".tex" => TemplateFormat.Latex,
                _ => throw new UnsupportedFormatException(
                    $"Unsupported template format: '{extension}'. Supported: .docx, .xlsx, .tex")
            };
        }
    }
}
