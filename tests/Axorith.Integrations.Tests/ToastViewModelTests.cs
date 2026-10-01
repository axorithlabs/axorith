using System.Reactive.Linq;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Sdk.Services;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class ToastViewModelTests
{
    [Theory]
    [InlineData(NotificationType.Success, "Presets", "Preset saved successfully")]
    [InlineData(NotificationType.Error, "Sessions", "Failed to start session: unavailable")]
    [InlineData(NotificationType.Warning, "Site Blocker", "No sites configured")]
    public void ServicePreservesSourceCategoryAndEntireMessage(NotificationType type, string category, string message)
    {
        using var service = new Axorith.Client.Services.ToastNotificationService();
        ToastViewModel? received = null;
        using var subscription = service.Notifications.Subscribe(
            notification => received = new ToastViewModel(notification));
        service.Show(message, type, category);
        Assert.NotNull(received);
        Assert.Equal(category, received.Title);
        Assert.Equal(category.ToUpperInvariant(), received.DesktopTitle);
        Assert.Equal(message, received.Body);
    }

    [Theory]
    [InlineData(NotificationType.Info, "Info text", "Axorith", "Info text")]
    [InlineData(NotificationType.Success, "Preset saved successfully", "Axorith", "Preset saved successfully")]
    [InlineData(NotificationType.Warning, "Warning text", "Axorith", "Warning text")]
    [InlineData(NotificationType.Error, "Error text", "Axorith", "Error text")]
    [InlineData(NotificationType.Error, "Steam: Path not configured.", "Steam", "Path not configured.")]
    public void Notification_UsesCategoryForPlainMessagesAndKeepsExplicitTitles(
        NotificationType type, string message, string expectedTitle, string expectedBody)
    {
        var viewModel = new ToastViewModel(new ToastNotification(message, type, Guid.NewGuid()));

        Assert.Equal(expectedTitle, viewModel.Title);
        Assert.Equal(expectedTitle.ToUpperInvariant(), viewModel.DesktopTitle);
        Assert.Equal(expectedBody, viewModel.Body);
    }
}
