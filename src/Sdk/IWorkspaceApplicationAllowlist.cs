namespace Axorith.Sdk;

/// <summary>Receives the executable names configured as workspace applications for this session.</summary>
public interface IWorkspaceApplicationAllowlist
{
    /// <summary>Sets the executable names selected in the current workspace.</summary>
    /// <param name="processNames">Executable names or paths for this workspace's launcher modules.</param>
    void SetWorkspaceApplications(IEnumerable<string> processNames);
}
