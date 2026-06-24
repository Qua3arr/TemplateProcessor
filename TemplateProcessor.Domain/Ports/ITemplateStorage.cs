using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TemplateProcessor.Domain.Ports
{
    public interface ITemplateStorage
    {
        Task<Stream> ReadAsync(string templatePath, CancellationToken cancellationToken = default);
    }
}
