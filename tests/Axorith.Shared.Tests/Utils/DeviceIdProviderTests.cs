using Axorith.Shared.Utils;

namespace Axorith.Shared.Tests.Utils;

public sealed class DeviceIdProviderTests
{
    [Fact]
    public async Task GetDeviceId_PersistsOneRandomIdentityForConcurrentCallers()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-identity-");
        var path = Path.Combine(directory.FullName, "installation-id.txt");

        try
        {
            var ids = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => DeviceIdProvider.GetDeviceId(path))));

            Assert.All(ids, id => Assert.Equal(ids[0], id));
            Assert.True(Guid.TryParse(ids[0], out _));
            Assert.Equal(ids[0], await File.ReadAllTextAsync(path));
            Assert.NotEqual(Environment.MachineName, ids[0]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
