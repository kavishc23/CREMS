using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CREMS.Api.Services;

// JSON permits numeric enum values; validate nested request DTOs before actions run.
public sealed class RequestEnumValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? value, string path, int depth)
        {
            if (value is null || depth > 12) return;
            var type = value.GetType();
            if (type.IsEnum)
            {
                var valid = Enum.IsDefined(type, value);
                if (!valid && type.IsDefined(typeof(FlagsAttribute), false))
                {
                    var allowed = Enum.GetValues(type).Cast<object>().Aggregate(0L, (bits, item) => bits | Convert.ToInt64(item));
                    valid = (Convert.ToInt64(value) & ~allowed) == 0;
                }
                if (!valid) context.ModelState.AddModelError(path, "Select a supported value.");
                return;
            }
            if (value is string || type.IsValueType || value is IFormFile || !visited.Add(value)) return;
            if (value is IEnumerable collection)
            {
                var index = 0;
                foreach (var item in collection) Visit(item, $"{path}[{index++}]", depth + 1);
            }
            else if (type.Assembly == typeof(RequestEnumValidationFilter).Assembly)
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.CanRead && x.GetIndexParameters().Length == 0))
                    Visit(property.GetValue(value), $"{path}.{property.Name}", depth + 1);
        }
        foreach (var (name, value) in context.ActionArguments) Visit(value, name, 0);
        if (!context.ModelState.IsValid) context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
