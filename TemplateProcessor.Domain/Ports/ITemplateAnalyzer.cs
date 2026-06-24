using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Domain.Ports
{
    public interface ITemplateAnalyzer
    {
        //Анализирует шаблон и возвращает список всех переменных, которые встречаются в нём.
        Task<IReadOnlyList<TemplateVariable>> AnalyzeAsync(
            Stream templateStream,
            TemplateFormat format,
            CancellationToken cancellationToken = default);
    }
}
