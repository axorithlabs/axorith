namespace Axorith.Core.Models;

public class SessionPreset
{
    public SessionPreset()
    {
    }

    public SessionPreset(Guid id)
    {
        Id = id;
    }

    public SessionPreset(SessionPreset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Version = source.Version;
        Id = source.Id;
        Name = source.Name;
        FocusCommitment = new FocusCommitmentOptions(source.FocusCommitment);
        Modules = [.. source.Modules.Select(module => new ConfiguredModule(module))];
    }

    public int Version { get; set; } = 3;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public FocusCommitmentOptions FocusCommitment { get; set; } = new();

    public List<ConfiguredModule> Modules { get; set; } = [];
}
