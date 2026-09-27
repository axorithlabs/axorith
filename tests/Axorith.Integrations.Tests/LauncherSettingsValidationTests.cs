using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class LauncherSettingsValidationTests
{
    [Fact]
    public async Task LaunchOrAttachRejectsMissingExecutableDuringPreflight()
    {
        using var settings = new TestSettings();
        settings.ApplicationPath.SetValue(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"));
        settings.ProcessMode.SetValue("LaunchOrAttach");

        var result = await settings.ValidateAsync();

        Assert.Equal(ValidationStatus.Error, result.Status);
        Assert.Contains("File not found", result.FieldErrors["ApplicationPath"]);
    }

    [Fact]
    public async Task AttachExistingCanUseAnAlreadyRunningApplicationWithoutItsFile()
    {
        using var settings = new TestSettings();
        settings.ApplicationPath.SetValue(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"));
        settings.ProcessMode.SetValue("AttachExisting");

        var result = await settings.ValidateAsync();

        Assert.Equal(ValidationStatus.Ok, result.Status);
    }

    private sealed class TestSettings : LauncherSettingsBase
    {
        public override Setting<string> ApplicationPath { get; } = Setting.AsText(
            "ApplicationPath", "Application", string.Empty);

        protected override IEnumerable<ISetting> GetAdditionalSettings() => [];
        protected override IEnumerable<Axorith.Sdk.Actions.IAction> GetAdditionalActions() => [];
    }
}
