using HCS.Passwordless.Core.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCS.Passwordless.Tests.Filters;

public class EnumAsIntegerJsonAttributeTests
{
    private readonly EnumAsIntegerJsonAttribute _sut = new();

    private enum TestEnum
    {
        FirstValue = 0,
        SecondValue = 1,
        ThirdValue = 2
    }

    private record TestDto(TestEnum Status, string Name);

    private ActionExecutedContext CreateActionExecutedContext(IActionResult result)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        var actionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);
        return new ActionExecutedContext(actionContext, [], null!)
        {
            Result = result
        };
    }

    [Fact]
    public void OnActionExecuted_WithObjectResultContainingExistingJsonFormatter_ReplaceFormatterWithEnumAsInteger()
    {
        // Arrange
        var existingFormatter = new SystemTextJsonOutputFormatter(JsonSerializerOptions.Default);
        var objectResult = new ObjectResult(new TestDto(TestEnum.SecondValue, "test"))
        {
            Formatters = { existingFormatter }
        };
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        objectResult.Formatters.Should().HaveCount(1);
        var formatter = objectResult.Formatters.OfType<SystemTextJsonOutputFormatter>().FirstOrDefault();
        formatter.Should().NotBeNull();
        formatter.Should().NotBe(existingFormatter);
    }

    [Fact]
    public void OnActionExecuted_WithObjectResultWithoutJsonFormatter_InsertJsonFormatterAtHighestPriority()
    {
        // Arrange
        var otherFormatter = Substitute.For<IOutputFormatter>();
        var objectResult = new ObjectResult(new TestDto(TestEnum.FirstValue, "test"))
        {
            Formatters = { otherFormatter }
        };
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        objectResult.Formatters.Should().HaveCount(2);
        objectResult.Formatters[0].Should().BeOfType<SystemTextJsonOutputFormatter>();
        objectResult.Formatters[1].Should().Be(otherFormatter);
    }

    [Fact]
    public void OnActionExecuted_WithObjectResultAndNoExistingFormatters_InsertJsonFormatter()
    {
        // Arrange
        var objectResult = new ObjectResult(new TestDto(TestEnum.FirstValue, "test"));
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        objectResult.Formatters.Should().HaveCount(1);
        objectResult.Formatters[0].Should().BeOfType<SystemTextJsonOutputFormatter>();
    }

    [Fact]
    public void OnActionExecuted_WithNonObjectResult_DoesNotModifyResult()
    {
        // Arrange
        var statusCodeResult = new OkResult();
        var context = CreateActionExecutedContext(statusCodeResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        context.Result.Should().Be(statusCodeResult);
    }

    [Fact]
    public void OnActionExecuted_WithNullResult_DoesNotThrow()
    {
        // Arrange
        var context = CreateActionExecutedContext(null!);

        // Act
        var action = () => _sut.OnActionExecuted(context);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void OnActionExecuted_ConfiguresJsonFormatterWithCamelCaseNamingPolicy()
    {
        // Arrange
        var objectResult = new ObjectResult(new { FirstName = "John", LastName = "Doe" });
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        var formatter = objectResult.Formatters.OfType<SystemTextJsonOutputFormatter>().FirstOrDefault();
        formatter.Should().NotBeNull();
        formatter!.SerializerOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void OnActionExecuted_SerializesEnumsAsIntegersNotStrings()
    {
        // Arrange
        var objectResult = new ObjectResult(new TestDto(TestEnum.SecondValue, "test"));
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        var formatter = objectResult.Formatters.OfType<SystemTextJsonOutputFormatter>().FirstOrDefault();
        formatter.Should().NotBeNull();

        // Verify that the formatter options do NOT have JsonStringEnumConverter
        var hasStringEnumConverter = formatter!.SerializerOptions.Converters
            .Any(c => c.GetType() == typeof(JsonStringEnumConverter));
        hasStringEnumConverter.Should().BeFalse();
    }

    [Fact]
    public void OnActionExecuted_WithMultipleFormatters_PreservesNonJsonFormatters()
    {
        // Arrange
        var jsonFormatter = new SystemTextJsonOutputFormatter(JsonSerializerOptions.Default);
        var xmlFormatter = Substitute.For<IOutputFormatter>();
        var textFormatter = Substitute.For<IOutputFormatter>();

        var objectResult = new ObjectResult(new TestDto(TestEnum.FirstValue, "test"))
        {
            Formatters = { xmlFormatter, jsonFormatter, textFormatter }
        };
        var context = CreateActionExecutedContext(objectResult);
        var initialFormatterCount = objectResult.Formatters.Count;

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        // Verify that the count is preserved - no formatters added or removed beyond replacement
        objectResult.Formatters.Should().HaveCount(initialFormatterCount);
        // Verify that a JSON formatter exists at position 1 (where it was before)
        objectResult.Formatters[1].Should().BeOfType<SystemTextJsonOutputFormatter>();
        // Verify first formatter is still the XML formatter
        objectResult.Formatters[0].Should().Be(xmlFormatter);
        // Verify last formatter is still the text formatter
        objectResult.Formatters[2].Should().Be(textFormatter);
    }

    [Fact]
    public void OnActionExecuted_WithObjectResultContainingMultipleJsonFormatters_ReplacesFirstOne()
    {
        // Arrange
        var formatter1 = new SystemTextJsonOutputFormatter(JsonSerializerOptions.Default);
        var formatter2 = new SystemTextJsonOutputFormatter(JsonSerializerOptions.Default);

        var objectResult = new ObjectResult(new TestDto(TestEnum.FirstValue, "test"))
        {
            Formatters = { formatter1, formatter2 }
        };
        var context = CreateActionExecutedContext(objectResult);

        // Act
        _sut.OnActionExecuted(context);

        // Assert
        objectResult.Formatters.Should().HaveCount(2);
        objectResult.Formatters[0].Should().BeAssignableTo<SystemTextJsonOutputFormatter>();
        objectResult.Formatters[1].Should().Be(formatter2);

        var replacedFormatter = objectResult.Formatters[0] as SystemTextJsonOutputFormatter;
        replacedFormatter.Should().NotBe(formatter1);
        replacedFormatter!.SerializerOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }
}
