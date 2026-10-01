using Axorith.Host.Mappers;
using Google.Protobuf;
using Xunit;
using SdkAction = Axorith.Sdk.Actions.Action;

namespace Axorith.Host.Tests;

public sealed class ActionMapperTests
{
    [Fact]
    public void SettingKeySurvivesActionMappingAndProtobufSerialization()
    {
        using var action = SdkAction.Create("TestConnection", "Test Connection", settingKey: "BaseUrl");

        var message = ActionMapper.ToMessage(action);
        var roundTrip = Axorith.Contracts.Action.Parser.ParseFrom(message.ToByteArray());

        Assert.Equal("BaseUrl", roundTrip.SettingKey);
    }
}
