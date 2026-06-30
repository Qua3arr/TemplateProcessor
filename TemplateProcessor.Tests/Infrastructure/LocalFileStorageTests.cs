using Microsoft.Extensions.Logging;
using TemplateProcessor.Infrastructure.Storage;

namespace TemplateProcessor.Tests.Infrastructure
{
    public class LocalFileStorageTests : IDisposable
    {
        private readonly string _rootDirectory;
        private readonly string _baseDirectory;
        private readonly string _outsideDirectory;

        public LocalFileStorageTests()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "TemplateProcessorTests", Guid.NewGuid().ToString("N"));
            _baseDirectory = Path.Combine(_rootDirectory, "base");
            _outsideDirectory = Path.Combine(_rootDirectory, "base-other");

            Directory.CreateDirectory(_baseDirectory);
            Directory.CreateDirectory(_outsideDirectory);
        }

        [Fact]
        public async Task ReadAsync_WhenRelativePathIsInsideBaseDirectory_ReturnsFileContent()
        {
            var logger = new TestLogger<LocalFileStorage>();
            var storage = new LocalFileStorage(_baseDirectory, logger);
            var templateDirectory = Path.Combine(_baseDirectory, "Templates");
            var templatePath = Path.Combine(templateDirectory, "sample.txt");

            Directory.CreateDirectory(templateDirectory);
            await File.WriteAllTextAsync(templatePath, "hello");

            await using var stream = await storage.ReadAsync(Path.Combine("Templates", "sample.txt"));
            using var reader = new StreamReader(stream);

            Assert.Equal("hello", await reader.ReadToEndAsync());
            Assert.Contains(logger.Entries, entry =>
                entry.Level == LogLevel.Information &&
                entry.Message.Contains("Reading template file"));
            Assert.Contains(logger.Entries, entry =>
                entry.Level == LogLevel.Information &&
                entry.Message.Contains("was read successfully"));
        }

        [Fact]
        public async Task ReadAsync_WhenRelativePathEscapesBaseDirectory_ThrowsUnauthorizedAccessException()
        {
            var logger = new TestLogger<LocalFileStorage>();
            var storage = new LocalFileStorage(_baseDirectory, logger);
            var outsidePath = Path.Combine(_outsideDirectory, "secret.txt");

            await File.WriteAllTextAsync(outsidePath, "secret");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => storage.ReadAsync(Path.Combine("..", "base-other", "secret.txt")));

            Assert.Contains(logger.Entries, entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("outside base directory"));
        }

        [Fact]
        public async Task ReadAsync_WhenAbsolutePathOnlySharesBaseDirectoryPrefix_ThrowsUnauthorizedAccessException()
        {
            var logger = new TestLogger<LocalFileStorage>();
            var storage = new LocalFileStorage(_baseDirectory, logger);
            var outsidePath = Path.Combine(_outsideDirectory, "template.txt");

            await File.WriteAllTextAsync(outsidePath, "outside");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => storage.ReadAsync(outsidePath));
        }

        [Fact]
        public async Task ReadAsync_WhenFileIsMissing_LogsWarningAndThrowsFileNotFoundException()
        {
            var logger = new TestLogger<LocalFileStorage>();
            var storage = new LocalFileStorage(_baseDirectory, logger);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => storage.ReadAsync("missing.txt"));

            Assert.Contains(logger.Entries, entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("was not found"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootDirectory))
                    Directory.Delete(_rootDirectory, true);
            }
            catch
            {
            }
        }

        private sealed class TestLogger<T> : ILogger<T>
        {
            public List<LogEntry> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }

        private sealed record LogEntry(LogLevel Level, string Message);
    }
}
