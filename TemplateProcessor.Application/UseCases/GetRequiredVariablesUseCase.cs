using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Application.UseCases
{
    public class GetRequiredVariablesUseCase
    {
        private readonly ITemplateStorage _storage;
        private readonly ITemplateFormatResolver _formatResolver;
        private readonly ITemplateAnalyzerFactory _analyzerFactory;

        public GetRequiredVariablesUseCase(
            ITemplateStorage storage,
            ITemplateFormatResolver formatResolver,
            ITemplateAnalyzerFactory analyzerFactory)
        {
            _storage = storage;
            _formatResolver = formatResolver;
            _analyzerFactory = analyzerFactory;
        }

        //Выполняет анализ шаблона.
        public async Task<IReadOnlyList<TemplateVariable>> ExecuteAsync(
            string templatePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var format = _formatResolver.GetTemplateFormat(templatePath);

                await using var stream = await _storage.ReadAsync(templatePath, cancellationToken);

                var analyzer = _analyzerFactory.Create(format);

                return await analyzer.AnalyzeAsync(stream, format, cancellationToken);
            }
            catch (UnsupportedFormatException)
            {
                throw;
            }
            catch (TemplateParsingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TemplateParsingException($"Error during template analysis: {ex.Message}", ex);
            }
        }
    }
}
