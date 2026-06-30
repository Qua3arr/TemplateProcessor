using System.Diagnostics;
using System.Text;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Renderers;

namespace TemplateProcessor.Tests.Performance
{
    public class RendererPerformanceTests
    {
        private const long MinDocumentSizeBytes = 5L * 1024 * 1024;
        private const long MaxDocumentSizeBytes = 10L * 1024 * 1024;
        private static readonly TimeSpan MaxRenderDuration = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task LatexRenderer_RenderLargeTexDocument_CompletesWithinPerformanceBudget()
        {
            var renderer = new LatexRenderer();
            var template = string.Join(
                Environment.NewLine,
                "\\documentclass{article}",
                "\\begin{document}",
                "{{#Rows}}",
                "{{Index}} & {{Payload}} \\\\",
                "{{/Rows}}",
                "\\end{document}");

            using var templateStream = new MemoryStream(Encoding.UTF8.GetBytes(template));
            var context = new TemplateContext
            {
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
                {
                    ["Rows"] = CreateRows(rowCount: 3200, payloadLength: 1800)
                }
            };

            var stopwatch = Stopwatch.StartNew();
            using var resultStream = await renderer.RenderAsync(
                templateStream,
                context,
                TemplateFormat.Latex,
                OutputFormat.Tex);
            stopwatch.Stop();

            Assert.InRange(resultStream.Length, MinDocumentSizeBytes, MaxDocumentSizeBytes);
            Assert.True(
                stopwatch.Elapsed <= MaxRenderDuration,
                $"Large document render took {stopwatch.Elapsed.TotalSeconds:F2}s, expected <= {MaxRenderDuration.TotalSeconds:F0}s. Output size: {resultStream.Length} bytes.");
        }

        private static List<Dictionary<string, object>> CreateRows(int rowCount, int payloadLength)
        {
            var payload = new string('x', payloadLength);
            var rows = new List<Dictionary<string, object>>(rowCount);

            for (var i = 1; i <= rowCount; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["Index"] = i,
                    ["Payload"] = payload
                });
            }

            return rows;
        }
    }
}
