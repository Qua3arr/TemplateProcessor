namespace TemplateProcessor.Domain.Exceptions
{
    //если отсутствует значение для переменной из шаблона
    public class MissingDataException : Exception
    {
        public MissingDataException(string variableName)
            : base($"Missing data for required variable: '{variableName}'")
        {
        }
    }
}
