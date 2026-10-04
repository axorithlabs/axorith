namespace Axorith.Core.Models;

public class ConfiguredModule
{
    public ConfiguredModule() { }

    public ConfiguredModule(ConfiguredModule source)
    {
        ArgumentNullException.ThrowIfNull(source);
        InstanceId = source.InstanceId;
        ModuleId = source.ModuleId;
        CustomName = source.CustomName;
        StartDelay = source.StartDelay;
        Settings = new Dictionary<string, string>(source.Settings);
    }

    public Guid InstanceId { get; set; } = Guid.NewGuid();

    public Guid ModuleId { get; set; }

    public string? CustomName { get; set; }

    public TimeSpan StartDelay { get; set; } = TimeSpan.Zero;

    public Dictionary<string, string> Settings { get; set; } = [];
}
