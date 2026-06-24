using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.ValueObjects;

//Фасад модуля
namespace TemplateProcessor.Application.Abstractions
{
    public class TemplateEngineModule : ITemplateEngineModule
    {
        private readonly GetRequiredVariablesUseCase _getVariablesUseCase;
        private readonly RenderDocumentUseCase _renderDocumentUseCase;

        public TemplateEngineModule(
            GetRequiredVariablesUseCase getVariablesUseCase,
            RenderDocumentUseCase renderDocumentUseCase)
        {
            _getVariablesUseCase = getVariablesUseCase;
            _renderDocumentUseCase = renderDocumentUseCase;
        }

        public async Task<IReadOnlyList<TemplateVariable>> GetRequiredVariablesAsync(
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            return await _getVariablesUseCase.ExecuteAsync(templatePath, cancellationToken);
        }

        public async Task<Stream> RenderDocumentAsync(
            string templatePath,
            OutputFormat outputFormat,
            TemplateContext context,
            CancellationToken cancellationToken = default)
        {
            return await _renderDocumentUseCase.ExecuteAsync(
                templatePath,
                outputFormat,
                context,
                cancellationToken);
        }
    }
}
