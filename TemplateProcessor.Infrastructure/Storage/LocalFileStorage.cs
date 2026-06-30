using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TemplateProcessor.Domain.Ports;

namespace TemplateProcessor.Infrastructure.Storage
{
    public class LocalFileStorage : ITemplateStorage
    {
        private readonly string? _baseDirectory;
        private readonly string? _baseDirectoryWithSeparator;
        private readonly ILogger<LocalFileStorage> _logger;

        public LocalFileStorage(ILogger<LocalFileStorage> logger)
            : this(null, logger)
        {
        }

        public LocalFileStorage(string? baseDirectory = null, ILogger<LocalFileStorage>? logger = null)
        {
            _baseDirectory = baseDirectory == null ? null : Path.GetFullPath(baseDirectory);
            _baseDirectoryWithSeparator = _baseDirectory == null ? null : EnsureTrailingDirectorySeparator(_baseDirectory);
            _logger = logger ?? NullLogger<LocalFileStorage>.Instance;
        }

        public async Task<Stream> ReadAsync(string templatePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                _logger.LogWarning("Template path is empty.");
                throw new ArgumentException("Template path cannot be empty.", nameof(templatePath));
            }

            _logger.LogInformation("Reading template file from path '{TemplatePath}'.", templatePath);

            var fullPath = ResolveFullPath(templatePath);
            _logger.LogDebug("Resolved template path '{TemplatePath}' to '{FullPath}'.", templatePath, fullPath);

            if (_baseDirectory != null)
            {
                if (!IsPathInsideBaseDirectory(fullPath))
                {
                    _logger.LogWarning(
                        "Rejected template path '{FullPath}' because it is outside base directory '{BaseDirectory}'.",
                        fullPath,
                        _baseDirectory);

                    throw new UnauthorizedAccessException(
                        $"Access to file '{fullPath}' is not allowed. Base directory: '{_baseDirectory}'");
                }
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Template file '{FullPath}' was not found.", fullPath);
                throw new FileNotFoundException($"Template file not found: '{fullPath}'");
            }

            var memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(fullPath))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }

            memoryStream.Position = 0;
            _logger.LogInformation(
                "Template file '{FullPath}' was read successfully. Size: {Size} bytes.",
                fullPath,
                memoryStream.Length);

            return memoryStream;
        }

        private string ResolveFullPath(string templatePath)
        {
            if (_baseDirectory == null || Path.IsPathRooted(templatePath))
                return Path.GetFullPath(templatePath);

            return Path.GetFullPath(Path.Combine(_baseDirectory, templatePath));
        }

        private bool IsPathInsideBaseDirectory(string fullPath)
        {
            if (_baseDirectory == null || _baseDirectoryWithSeparator == null)
                return true;

            return string.Equals(fullPath, _baseDirectory, GetPathComparison()) ||
                   fullPath.StartsWith(_baseDirectoryWithSeparator, GetPathComparison());
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
            return OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
