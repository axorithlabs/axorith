namespace Axorith.Sdk;

/// <summary>Receives the executable names configured as workspace applications for this session.</summary>
public interface IWorkspaceApplicationAllowlist
{
    /// <summary>Sets the executable names selected in the current workspace.</summary>
    void SetWorkspaceApplications(IEnumerable<string> processNames);
}
