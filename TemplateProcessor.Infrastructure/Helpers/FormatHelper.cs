using System;
using System.IO;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.ValueObjects;

//Вспомогательный класс для работы с форматами файлов
namespace TemplateProcessor.Infrastructure.Helpers
{
    public static class FormatHelper
    {
        //Определяет формат шаблона по расширению файла
        public static TemplateFormat GetTemplateFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant(); 
            return extension switch
            {
                ".docx" => TemplateFormat.Word,
                ".xlsx" => TemplateFormat.Excel,
                ".tex" => TemplateFormat.Latex,
                _ => throw new UnsupportedFormatException(
                    $"Unsupported template format: '{extension}'. Supported: .docx, .xlsx, .tex")
            };
        }

        //Проверяет, поддерживается ли расширение файла
        public static bool IsSupportedFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".docx" or ".xlsx" or ".tex";
        }
    }
}
