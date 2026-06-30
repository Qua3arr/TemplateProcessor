using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public async Task<IReadOnlyList<TemplateVariableDto>> GetRequiredVariablesAsync(
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            var variables = await _getVariablesUseCase.ExecuteAsync(templatePath, cancellationToken);

            return variables
                .Select(v => new TemplateVariableDto
                {
                    Name = v.Name,
                    Type = v.Type
                })
                .ToList();
        }

        public async Task<Stream> RenderDocumentAsync(
            string templatePath,
            OutputFormat outputFormat,
            TemplateContextDto context,
            CancellationToken cancellationToken = default)
        {
            var domainContext = new TemplateContext
            {
                Scalars = context.Scalars,
                Collections = context.Collections
            };

            return await _renderDocumentUseCase.ExecuteAsync(
                templatePath,
                outputFormat,
                domainContext,
                cancellationToken);
        }
    }
}
