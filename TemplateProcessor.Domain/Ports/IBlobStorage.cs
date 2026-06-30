using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TemplateProcessor.Domain.Ports
{
    public interface IBlobStorage
    {
        Task UploadAsync(
            string objectKey,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default);

        Task CheckWriteAsync(CancellationToken cancellationToken = default);
    }
}
