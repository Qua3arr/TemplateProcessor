using System.Collections.Generic;
using System.Linq;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.ValueObjects;

//Сервис валидации данных, проверяет, что все переменный из шаблона присутствуют в контексте данных
namespace TemplateProcessor.Domain.Services
{
    public static class TemplateValidationService
    {
        public static void Validate(IReadOnlyList<TemplateVariable> variables, TemplateContext context)
        {
            //проверяем скалярные переменные
            var scalarVariables = variables.Where(v => v.Type == VariableType.Scalar).ToList();
            foreach (var variable in scalarVariables)
            {
                if (!context.Scalars.ContainsKey(variable.Name))
                {
                    throw new MissingDataException(variable.Name);
                }
            }

            //Проверяем коллекционные переменные
            var collectionVariables = variables.Where(v => v.Type == VariableType.Collection).ToList();
            foreach (var variable in collectionVariables)
            {
                if (!context.Collections.ContainsKey(variable.Name))
                {
                    throw new MissingDataException(variable.Name);
                }
            }
        }
    }
}
