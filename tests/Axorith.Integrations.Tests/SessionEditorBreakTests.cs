using System.Runtime.CompilerServices;
using Axorith.Client.ViewModels;
using Axorith.Core.Models;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class SessionEditorBreakTests
{
    [Fact]
    public void SelectingCustomBreakBudgetKeepsCustomControlsVisible()
    {
        var editor = (SessionEditorViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SessionEditorViewModel));
        typeof(SessionEditorViewModel).GetField("_focusCommitment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(editor, new FocusCommitmentOptions());

        editor.BreakPresetIndex = 2;

        Assert.Equal(2, editor.BreakPresetIndex);
        Assert.True(editor.IsCustomBreakBudget);
    }
}
