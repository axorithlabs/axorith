using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests;

/// <summary>
///     Tests for ModuleSettingDefinition positional record
/// </summary>
public class ModuleSettingDefinitionTests
{
    [Fact]
    public void Constructor_WithAllParameters_ShouldInitializeCorrectly()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("key1", "Value 1"),
            new("key2", "Value 2")
        };

        // Act
        var definition = new ModuleSettingDefinition(
            Key: "test-key",
            Label: "Test Label",
            Description: "Test Description",
            ControlType: SettingControlType.Text,
            Persistence: SettingPersistence.Persisted,
            IsVisible: true,
            IsReadOnly: false,
            ValueTypeName: "System.String",
            RawValue: "test value",
            Choices: choices
        );

        // Assert
        definition.Key.Should().Be("test-key");
        definition.Label.Should().Be("Test Label");
        definition.Description.Should().Be("Test Description");
        definition.ControlType.Should().Be(SettingControlType.Text);
        definition.Persistence.Should().Be(SettingPersistence.Persisted);
        definition.IsVisible.Should().BeTrue();
        definition.IsReadOnly.Should().BeFalse();
        definition.ValueTypeName.Should().Be("System.String");
        definition.RawValue.Should().Be("test value");
        definition.Choices.Should().BeEquivalentTo(choices);
    }

    [Fact]
    public void Deconstruction_ShouldExtractAllProperties()
    {
        // Arrange
        var definition = new ModuleSettingDefinition(
            "key",
            "label",
            "desc",
            SettingControlType.Text,
            SettingPersistence.Persisted,
            true,
            false,
            "string",
            "value",
            Array.Empty<KeyValuePair<string, string>>()
        );

        // Act
        var (key, label, description, controlType, persistence, isVisible, isReadOnly, valueTypeName, rawValue, choices
            ) = definition;

        // Assert
        key.Should().Be("key");
        label.Should().Be("label");
        description.Should().Be("desc");
        controlType.Should().Be(SettingControlType.Text);
        persistence.Should().Be(SettingPersistence.Persisted);
        isVisible.Should().BeTrue();
        isReadOnly.Should().BeFalse();
        valueTypeName.Should().Be("string");
        rawValue.Should().Be("value");
        choices.Should().BeEmpty();
    }

    [Fact]
    public void Equality_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };

        var def1 = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", choices);

        var def2 = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", choices);

        // Assert
        def1.Should().Be(def2);
        (def1 == def2).Should().BeTrue();
    }

    [Fact]
    public void Equality_WithDifferentValues_ShouldNotBeEqual()
    {
        // Arrange
        var def1 = new ModuleSettingDefinition(
            "key1", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        var def2 = new ModuleSettingDefinition(
            "key2", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        def1.Should().NotBe(def2);
        (def1 == def2).Should().BeFalse();
    }

    [Fact]
    public void With_Expression_ShouldCreateModifiedCopy()
    {
        // Arrange
        var original = new ModuleSettingDefinition(
            "key", "original", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Act
        var modified = original with { Label = "modified" };

        // Assert
        modified.Should().NotBeSameAs(original);
        modified.Key.Should().Be(original.Key);
        modified.Label.Should().Be("modified");
        original.Label.Should().Be("original");
    }

    [Fact]
    public void GetHashCode_ForEqualRecords_ShouldBeEqual()
    {
        // Arrange
        var def1 = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        var def2 = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        def1.GetHashCode().Should().Be(def2.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnRepresentation()
    {
        // Arrange
        var definition = new ModuleSettingDefinition(
            "key", "label", null, SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Act
        var stringRep = definition.ToString();

        // Assert
        stringRep.Should().NotBeNullOrEmpty();
        stringRep.Should().Contain("ModuleSettingDefinition");
    }

    [Fact]
    public void Key_EmptyString_ShouldBeValid()
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "", "label", null, SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.Key.Should().BeEmpty();
    }

    [Fact]
    public void Description_Null_ShouldBeValid()
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", null, SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.Description.Should().BeNull();
    }

    [Fact]
    public void Choices_EmptyList_ShouldBeValid()
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Choice, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.Choices.Should().NotBeNull();
        definition.Choices.Should().BeEmpty();
    }

    [Fact]
    public void Choices_MultipleItems_ShouldPreserveOrder()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("low", "Low"),
            new("medium", "Medium"),
            new("high", "High")
        };

        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Choice, SettingPersistence.Persisted,
            true, false, "string", "medium", choices);

        // Assert
        definition.Choices.Should().ContainInOrder(choices);
        definition.Choices.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(SettingControlType.Text)]
    [InlineData(SettingControlType.Number)]
    [InlineData(SettingControlType.Checkbox)]
    [InlineData(SettingControlType.Choice)]
    [InlineData(SettingControlType.Secret)]
    public void ControlType_AllTypes_ShouldBeValid(SettingControlType controlType)
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", controlType, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.ControlType.Should().Be(controlType);
    }

    [Theory]
    [InlineData(SettingPersistence.Persisted)]
    [InlineData(SettingPersistence.Ephemeral)]
    public void Persistence_AllTypes_ShouldBeValid(SettingPersistence persistence)
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, persistence,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.Persistence.Should().Be(persistence);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void IsVisibleAndIsReadOnly_AllCombinations_ShouldBeValid(bool isVisible, bool isReadOnly)
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            isVisible, isReadOnly, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.IsVisible.Should().Be(isVisible);
        definition.IsReadOnly.Should().Be(isReadOnly);
    }

    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Int32")]
    [InlineData("System.Boolean")]
    [InlineData("System.Decimal")]
    public void ValueTypeName_VariousTypes_ShouldBeValid(string typeName)
    {
        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, typeName, "value", Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.ValueTypeName.Should().Be(typeName);
    }

    [Fact]
    public void RawValue_LongString_ShouldBeValid()
    {
        // Arrange
        var longValue = new string('x', 10000);

        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", longValue, Array.Empty<KeyValuePair<string, string>>());

        // Assert
        definition.RawValue.Should().HaveLength(10000);
    }

    [Fact]
    public void Choices_WithDuplicateKeys_ShouldBeValid()
    {
        // Arrange - records don't enforce uniqueness
        var choices = new List<KeyValuePair<string, string>>
        {
            new("key", "Value 1"),
            new("key", "Value 2")
        };

        // Act
        var definition = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Choice, SettingPersistence.Persisted,
            true, false, "string", "value", choices);

        // Assert
        definition.Choices.Should().HaveCount(2);
    }

    [Fact]
    public void With_Expression_MultipleProperties_ShouldCreateCorrectCopy()
    {
        // Arrange
        var original = new ModuleSettingDefinition(
            "key", "label", "desc", SettingControlType.Text, SettingPersistence.Persisted,
            true, false, "string", "value", Array.Empty<KeyValuePair<string, string>>());

        // Act
        var modified = original with
        {
            Label = "new label",
            IsVisible = false,
            RawValue = "new value"
        };

        // Assert
        modified.Key.Should().Be(original.Key);
        modified.Label.Should().Be("new label");
        modified.IsVisible.Should().BeFalse();
        modified.RawValue.Should().Be("new value");
        modified.Description.Should().Be(original.Description);
    }
}