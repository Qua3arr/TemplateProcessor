using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Services;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Tests.Domain
{
    public class TemplateValidationServiceTests
    {
        [Fact]
        public void Validate_WhenAllScalarsExist_DoesNotThrow()
        {
            var variables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar),
            new("TotalSum", VariableType.Scalar)
        };

            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>
                {
                    ["ClientName"] = "ООО Ромашка",
                    ["TotalSum"] = 1000.50
                },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>()
            };

            var exception = Record.Exception(() => TemplateValidationService.Validate(variables, context));
            Assert.Null(exception);
        }

        [Fact]
        public void Validate_WhenScalarMissing_ThrowsMissingDataException()
        {
            var variables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar),
            new("TotalSum", VariableType.Scalar)
        };

            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>
                {
                    ["ClientName"] = "ООО Ромашка"
                },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>()
            };

            var exception = Assert.Throws<MissingDataException>(
                () => TemplateValidationService.Validate(variables, context));
            Assert.Contains("TotalSum", exception.Message);
        }

        [Fact]
        public void Validate_WhenCollectionMissing_ThrowsMissingDataException()
        {
            var variables = new List<TemplateVariable>
        {
            new("Items", VariableType.Collection)
        };

            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>(),
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>()
            };

            var exception = Assert.Throws<MissingDataException>(
                () => TemplateValidationService.Validate(variables, context));
            Assert.Contains("Items", exception.Message);
        }

        [Fact]
        public void Validate_WhenMixedVariablesAndAllPresent_DoesNotThrow()
        {
            var variables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar),
            new("Items", VariableType.Collection)
        };

            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>
                {
                    ["ClientName"] = "ООО Ромашка"
                },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
                {
                    ["Items"] = new List<Dictionary<string, object>>
                {
                    new() { ["Name"] = "Товар1", ["Price"] = 100 },
                    new() { ["Name"] = "Товар2", ["Price"] = 200 }
                }
                }
            };

            var exception = Record.Exception(() => TemplateValidationService.Validate(variables, context));
            Assert.Null(exception);
        }
    }
}
