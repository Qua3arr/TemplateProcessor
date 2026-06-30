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
        private readonly ITemplateFormatResolver _formatResolver;
        private readonly ITemplateAnalyzerFactory _analyzerFactory;
        private readonly IRenderingEngineFactory _renderingEngineFactory;

        public RenderDocumentUseCase(
            ITemplateStorage storage,
            ITemplateFormatResolver formatResolver,
            ITemplateAnalyzerFactory analyzerFactory,
            IRenderingEngineFactory renderingEngineFactory)
        {
            _storage = storage;
            _formatResolver = formatResolver;
            _analyzerFactory = analyzerFactory;
            _renderingEngineFactory = renderingEngineFactory;
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
                var inputFormat = _formatResolver.GetTemplateFormat(templatePath);

                await using var templateStream = await _storage.ReadAsync(templatePath, cancellationToken);

                var analyzer = _analyzerFactory.Create(inputFormat);
                var variables = await analyzer.AnalyzeAsync(templateStream, inputFormat, cancellationToken);

                TemplateValidationService.Validate(variables, context);

                templateStream.Seek(0, SeekOrigin.Begin);

                var renderer = _renderingEngineFactory.Create(inputFormat, outputFormat);
                var resultStream = await renderer.RenderAsync(
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
    }
}
