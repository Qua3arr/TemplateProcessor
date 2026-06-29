using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Exceptions;

//Конвертер LaTeX в PDF с использованием pdflatex
namespace TemplateProcessor.Infrastructure.Converters
{
    public class LatexToPdfConverter
    {
        private readonly string _latexCompilerPath;
        private readonly TimeSpan _timeout;

        public LatexToPdfConverter(string? latexCompilerPath = null, int timeoutSeconds = 60)
        {
            _latexCompilerPath = latexCompilerPath ?? "pdflatex";
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (!IsLatexCompilerAvailable())
            {
                throw new InvalidOperationException(
                    $"pdflatex is not installed or not available in PATH. " +
                    "Please install TeX Live (https://tug.org/texlive/) or MiKTeX (https://miktex.org/) " +
                    "and ensure 'pdflatex' is accessible, or specify the full path in the constructor.");
            }
        }

        private bool IsLatexCompilerAvailable()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _latexCompilerPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null) return false;
                process.WaitForExit(1000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }


        //Конвертирует поток
        public async Task<Stream> ConvertAsync(Stream texStream, CancellationToken cancellationToken = default)
        {
            if (texStream == null)
                throw new ArgumentNullException(nameof(texStream));

            if (!texStream.CanRead)
                throw new ArgumentException("Stream must be readable", nameof(texStream));

            //Создаём временную папку для работ
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string tempTexPath = Path.Combine(tempDir, "document.tex");
            string tempPdfPath = Path.Combine(tempDir, "document.pdf");

            try
            {
                Directory.CreateDirectory(tempDir);

                //Сохраняем входной поток
                await using (var fileStream = File.Create(tempTexPath))
                {
                    texStream.Position = 0;
                    await texStream.CopyToAsync(fileStream, cancellationToken);
                }

                await RunPdflatex(tempDir, tempTexPath, cancellationToken);

                if (!File.Exists(tempPdfPath))
                    throw new TemplateParsingException("PDF file was not created by pdflatex");

                var resultStream = new MemoryStream();
                await using (var pdfStream = File.OpenRead(tempPdfPath))
                {
                    await pdfStream.CopyToAsync(resultStream, cancellationToken);
                }
                resultStream.Position = 0;
                return resultStream;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch
                {

                }
            }
        }

        private async Task RunPdflatex(string workingDirectory, string texFilePath, CancellationToken cancellationToken)
        {
            var args = $"-interaction=nonstopmode -output-directory=\"{workingDirectory}\" \"{texFilePath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _latexCompilerPath,
                Arguments = args,
                WorkingDirectory = workingDirectory,
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
                catch { }
                tcs.TrySetCanceled(cts.Token);
            });

            process.Start();

            var timeoutTask = Task.Delay(_timeout, cts.Token);
            var processTask = Task.Run(() => process.WaitForExit((int)_timeout.TotalMilliseconds), cts.Token);

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"pdflatex compilation timed out after {_timeout.TotalSeconds} seconds.");
            }

            //Проверяем код возврата
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                throw new TemplateParsingException($"pdflatex exited with code {process.ExitCode}. Output: {output}\nError: {error}");
            }
        }
    }
}
