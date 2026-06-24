using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Application.Abstractions
{
    public interface ITemplateEngineModule
    {
        //Анализирует шаблон и возвращает список необходимых переменных.
        Task<IReadOnlyList<TemplateVariable>> GetRequiredVariablesAsync(
            string templatePath,
            CancellationToken cancellationToken = default);

        //Генерирует документ на основе шаблона и данных.
        Task<Stream> RenderDocumentAsync(
            string templatePath,
            OutputFormat outputFormat,
            TemplateContext context,
            CancellationToken cancellationToken = default);
    }
}
