using System.Reflection;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for new guard clauses added to Setting
///     Validates ArgumentException.ThrowIfNullOrWhiteSpace and ArgumentNullException.ThrowIfNull
/// </summary>
public class SettingGuardClauseTests
{

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsText_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        var act = () => Setting.AsText(invalidKey!, "Label", "value");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsText_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        var act = () => Setting.AsText("key", invalidLabel!, "value");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("label");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsCheckbox_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        var act = () => Setting.AsCheckbox(invalidKey!, "Label", false);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsNumber_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        var act = () => Setting.AsNumber(invalidKey!, "Label", 0m);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsChoice_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };

        var act = () => Setting.AsChoice(invalidKey!, "Label", "k", choices);

        act.Should().Throw<ArgumentException>();
    }



    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetLabel_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        var setting = Setting.AsText("key", "Label", "value");

        var act = () => setting.SetLabel(invalidLabel!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("newLabel");
    }



    [Fact]
    public void SetChoices_WithNull_ShouldThrow()
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        var act = () => setting.SetChoices(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("newChoices");
    }

    [Fact]
    public void SetChoices_WithEmptyList_ShouldNotThrow()
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        var act = () => setting.SetChoices([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithNullKeyInList_ShouldNotThrowButAcceptIt()
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        var newChoices = new List<KeyValuePair<string, string>>
        {
            new(null!, "Null Key")
        };

        var act = () => setting.SetChoices(newChoices);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithDuplicateKeys_ShouldNotThrowButAcceptThem()
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        var newChoices = new List<KeyValuePair<string, string>>
        {
            new("duplicate", "First"),
            new("duplicate", "Second")
        };

        var act = () => setting.SetChoices(newChoices);

        act.Should().NotThrow();
    }



    [Fact]
    public void InitializeChoices_WithNull_ShouldThrow()
    {
        // This tests internal method through AsChoice
        var act = () =>
        {
            // Use reflection to call internal method
            var setting = Setting.AsText("key", "Label", "value");
            var method = typeof(Setting<string>).GetMethod("InitializeChoices",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(setting, [null]);
        };

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }



    [Fact]
    public void AsChoice_WithKeyNotInChoices_ShouldStillCreate()
    {
        var choices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };

        var setting = Setting.AsChoice("choice", "Choice", "c", choices);

        setting.GetCurrentValue().Should().Be("c");
    }

    [Fact]
    public void MultipleSettings_WithSameKey_ShouldBeIndependent()
    {
        var setting1 = Setting.AsText("same-key", "Label 1", "value1");
        var setting2 = Setting.AsText("same-key", "Label 2", "value2");

        setting1.Key.Should().Be("same-key");
        setting2.Key.Should().Be("same-key");
        setting1.GetCurrentValue().Should().Be("value1");
        setting2.GetCurrentValue().Should().Be("value2");

        // Changing one should not affect the other
        setting1.SetValue("changed");
        setting2.GetCurrentValue().Should().Be("value2");
    }

    [Fact]
    public void Setting_WithVeryLongKey_ShouldWork()
    {
        var longKey = new string('k', 1000);

        var act = () => Setting.AsText(longKey, "Label", "value");

        act.Should().NotThrow();
    }

    [Fact]
    public void Setting_WithUnicodeKey_ShouldWork()
    {
        var unicodeKey = "キー_клавиша_🔑";

        var setting = Setting.AsText(unicodeKey, "Label", "value");

        setting.Key.Should().Be(unicodeKey);
    }

    [Fact]
    public void Setting_WithSpecialCharactersInKey_ShouldWork()
    {
        var specialKey = "key.with-special_chars@123";

        var setting = Setting.AsText(specialKey, "Label", "value");

        setting.Key.Should().Be(specialKey);
    }

}
