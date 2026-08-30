using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text.Json;

namespace HCS.Passwordless.Core.Filters;

/// <summary>
/// Ensures enums are serialized as integers, regardless of global configuration.
/// This overrides any application-wide JSON serialization settings that may serialize enums as strings.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class EnumAsIntegerJsonAttribute : ActionFilterAttribute
{
    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult)
        {
            var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                // Enums will serialize as integers by default when not using JsonStringEnumConverter
            };

            // Only replace or insert the JSON formatter, preserving others for content negotiation
            var jsonFormatterType = typeof(SystemTextJsonOutputFormatter);
            var existingJsonFormatter = objectResult.Formatters
                .OfType<SystemTextJsonOutputFormatter>()
                .FirstOrDefault();

            var newJsonFormatter = new SystemTextJsonOutputFormatter(options);

            if (existingJsonFormatter != null)
            {
                var idx = objectResult.Formatters.IndexOf(existingJsonFormatter);
                objectResult.Formatters[idx] = newJsonFormatter;
            }
            else
            {
                // Insert at highest priority for JSON, but do not clear other formatters
                objectResult.Formatters.Insert(0, newJsonFormatter);
            }
        }
    }
}
