using System.Collections.Concurrent;
using FluentAssertions;

namespace Axorith.Sdk.Tests;

public sealed class ValidationResultTests
{
    [Fact]
    public void SuccessIsAReusableValidResult()
    {
        ValidationResult.Success.Should().BeSameAs(ValidationResult.Success);
        ValidationResult.Success.Status.Should().Be(ValidationStatus.Ok);
        ValidationResult.Success.FieldErrors.Should().BeEmpty();
    }

    [Fact]
    public void FailPreservesGlobalAndFieldErrorsWithoutRetainingMutableInput()
    {
        var errors = new Dictionary<string, string> { ["AccessToken"] = "Required" };

        var result = ValidationResult.Fail(errors, "Configuration contains errors.");
        errors["AccessToken"] = "changed";

        result.Status.Should().Be(ValidationStatus.Error);
        result.Message.Should().Be("Configuration contains errors.");
        var fieldError = result.FieldErrors.Should().ContainSingle().Which;
        fieldError.Key.Should().Be("AccessToken");
        fieldError.Value.Should().Be("Required");
    }

    [Fact]
    public void FailAndWarnHaveDifferentStatusesAndPreserveMessages()
    {
        var error = ValidationResult.Fail("Invalid settings");
        var warning = ValidationResult.Warn("No blocking targets configured");

        error.Status.Should().Be(ValidationStatus.Error);
        error.Message.Should().Be("Invalid settings");
        warning.Status.Should().Be(ValidationStatus.Warning);
        warning.Message.Should().Be("No blocking targets configured");
    }

    [Fact]
    public void ConcurrentSuccessReadsReturnTheSameResult()
    {
        var results = new ConcurrentBag<ValidationResult>();

        Parallel.For(0, 100, _ => results.Add(ValidationResult.Success));

        results.Should().HaveCount(100);
        results.Should().OnlyContain(result => ReferenceEquals(result, ValidationResult.Success));
    }
}
