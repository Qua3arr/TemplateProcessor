using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Ports;

namespace TemplateProcessor.Infrastructure.Storage
{
    public class LocalFileStorage : ITemplateStorage
    {
        private readonly string? _baseDirectory;

        public LocalFileStorage(string? baseDirectory = null)
        {
            _baseDirectory = baseDirectory;
        }

        public async Task<Stream> ReadAsync(string templatePath, CancellationToken cancellationToken = default)
        {
            //проверяем, что путь в разрешённой директории
            var fullPath = Path.GetFullPath(templatePath);

            if (_baseDirectory != null)
            {
                var basePath = Path.GetFullPath(_baseDirectory);
                if (!fullPath.StartsWith(basePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        $"Access to file '{fullPath}' is not allowed. Base directory: '{basePath}'");
                }
            }

            //Проверяем существование файла
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Template file not found: '{fullPath}'");
            }

            //Читаем файл в память и возвращаем MemoryStream для возможности повторного чтения
            var memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(fullPath))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }

            //сбрасываем позицию
            memoryStream.Position = 0;
            return memoryStream;
        }
    }
}
