using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TemplateProcessor.Domain.ValueObjects;

//Заполняет шаблон данными и возвращает готовый документ в виде потока.
namespace TemplateProcessor.Domain.Ports
{
    public interface IDocumentRenderer
    {
        Task<Stream> RenderAsync(
            Stream templateStream,
            TemplateContext context,
            TemplateFormat inputFormat,
            OutputFormat outputFormat,
            CancellationToken cancellationToken = default);
    }
}
