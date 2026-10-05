using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Axorith.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class SessionEditorBreakTests
{
    [AvaloniaFact]
    public async Task SelectingCustomBreakBudgetShowsControlsAndInitializesABudget()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var editor = new SessionEditorViewModel(null!, null!, null!, null!, null!, services);
        Dispatcher.UIThread.RunJobs();
        await editor.InitializationTask;

        editor.BreakPresetIndex = 2;

        Assert.True(editor.IsCustomBreakBudget);
        Assert.Equal(1, editor.BreakCount);
        Assert.Equal(5, editor.BreakDurationMinutes);
    }
}
