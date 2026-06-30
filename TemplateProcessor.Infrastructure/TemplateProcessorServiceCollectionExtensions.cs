using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateProcessor.Application.Abstractions;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Infrastructure.Analyzers;
using TemplateProcessor.Infrastructure.Renderers;
using TemplateProcessor.Infrastructure.Storage;

namespace TemplateProcessor.Infrastructure
{
    public static class TemplateProcessorServiceCollectionExtensions
    {
        public static IServiceCollection AddTemplateProcessor(
            this IServiceCollection services,
            string? templatesBaseDirectory = null)
        {
            services.AddSingleton<WordTemplateAnalyzer>();
            services.AddSingleton<ExcelTemplateAnalyzer>();
            services.AddSingleton<LatexTemplateAnalyzer>();

            services.AddSingleton<WordRenderer>();
            services.AddSingleton<ExcelRenderer>();
            services.AddSingleton<LatexRenderer>();

            services.AddSingleton<ITemplateFormatResolver, TemplateFormatResolver>();
            services.AddSingleton<ITemplateAnalyzerFactory, TemplateAnalyzerFactory>();
            services.AddSingleton<IRenderingEngineFactory, RenderingEngineFactory>();
            services.AddSingleton<ITemplateStorage>(provider =>
                new LocalFileStorage(
                    templatesBaseDirectory,
                    provider.GetService<ILogger<LocalFileStorage>>()));

            services.AddTransient<GetRequiredVariablesUseCase>();
            services.AddTransient<RenderDocumentUseCase>();
            services.AddTransient<ITemplateEngineModule, TemplateEngineModule>();

            return services;
        }

        public static IServiceCollection AddMinioBlobStorage(
            this IServiceCollection services,
            MinioBlobStorageOptions options)
        {
            services.AddSingleton<IBlobStorage>(provider =>
                new MinioBlobStorage(
                    options,
                    provider.GetService<ILogger<MinioBlobStorage>>()));

            return services;
        }
    }
}
