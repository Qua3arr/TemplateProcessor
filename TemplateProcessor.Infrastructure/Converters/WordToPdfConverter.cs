using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Exceptions;

//Конвертер Word в PDF с использованием LibreOffice headless
namespace TemplateProcessor.Infrastructure.Converters
{
    public class WordToPdfConverter
    {
        private readonly string _libreOfficePath;
        private readonly TimeSpan _timeout;

        //Инициализирует конвертер
        public WordToPdfConverter(string? libreOfficePath = null, int timeoutSeconds = 60)
        {
            _libreOfficePath = libreOfficePath ?? "soffice";
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        //Конвертирует поток с документом
        public async Task<Stream> ConvertAsync(Stream docxStream, CancellationToken cancellationToken = default)
        {
            if (docxStream == null)
                throw new ArgumentNullException(nameof(docxStream));

            if (!docxStream.CanRead)
                throw new ArgumentException("Stream must be readable", nameof(docxStream));

            // Создаём временные файлы
            string tempInputPath = Path.GetTempFileName() + ".docx";
            string tempOutputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempOutputPath = Path.Combine(tempOutputDir, "output.pdf");

            try
            {
                await using (var fileStream = File.Create(tempInputPath))
                {
                    docxStream.Position = 0;
                    await docxStream.CopyToAsync(fileStream, cancellationToken);
                }

                Directory.CreateDirectory(tempOutputDir);

                await RunLibreOfficeConversion(tempInputPath, tempOutputDir, cancellationToken);

                //Проверяем, что PDF создался
                if (!File.Exists(tempOutputPath))
                    throw new TemplateParsingException("PDF file was not created by LibreOffice");

                var resultStream = new MemoryStream();
                await using (var pdfStream = File.OpenRead(tempOutputPath))
                {
                    await pdfStream.CopyToAsync(resultStream, cancellationToken);
                }
                resultStream.Position = 0;
                return resultStream;
            }
            finally
            {
                //Удаляем временные файлы
                try
                {
                    if (File.Exists(tempInputPath))
                        File.Delete(tempInputPath);
                    if (Directory.Exists(tempOutputDir))
                        Directory.Delete(tempOutputDir, true);
                }
                catch
                {

                }
            }
        }

        private async Task RunLibreOfficeConversion(string inputPath, string outputDir, CancellationToken cancellationToken)
        {
            var args = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{inputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _libreOfficePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo };

            var tcs = new TaskCompletionSource<bool>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            cts.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch {  }
                tcs.TrySetCanceled(cts.Token);
            });

            process.Start();

            var timeoutTask = Task.Delay(_timeout, cts.Token);
            var processTask = Task.Run(() => process.WaitForExit((int)_timeout.TotalMilliseconds), cts.Token);

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"LibreOffice conversion timed out after {_timeout.TotalSeconds} seconds.");
            }

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new TemplateParsingException($"LibreOffice exited with code {process.ExitCode}. Error: {error}");
            }
        }
    }
}
