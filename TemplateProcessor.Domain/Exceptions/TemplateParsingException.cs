namespace TemplateProcessor.Domain.Exceptions
{
    //для ошибки парсинга
    public class TemplateParsingException : Exception
    {
        public TemplateParsingException(string message, Exception? innerException = null)
            : base(message, innerException) { }
    }
}
