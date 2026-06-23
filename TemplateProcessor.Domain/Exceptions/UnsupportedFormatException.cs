namespace TemplateProcessor.Domain.Exceptions
{
    //Неподдерживаемый входной/выходной формат
    public class UnsupportedFormatException : Exception
    {
        public UnsupportedFormatException(string message) : base(message) { }
    }
}
