using PersonalFinance.Modules.Integrations.Application;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.UnitTests.Notifications;

public class WhatsAppTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_code_is_accepted_once_and_not_after_five_wrong_tries_or_ten_minutes()
    {
        var endpoint = ChannelEndpoint.WhatsAppOf(Guid.NewGuid());
        var code = endpoint.StartVerification("51987654321", Now).Value;
        var wrong = code == "000000" ? "111111" : "000000";

        Assert.Equal("notifications.code_too_soon", endpoint.StartVerification("51987654321", Now.AddSeconds(30)).Error.Code);
        for (var attempt = 0; attempt < ChannelEndpoint.MaxAttempts; attempt++)
        {
            Assert.Equal("notifications.code_invalid", endpoint.Confirm(wrong, Now).Error.Code);
        }

        // Five wrong tries: not even the right code works any more.
        Assert.Equal("notifications.code_attempts_exceeded", endpoint.Confirm(code, Now).Error.Code);

        code = endpoint.StartVerification("51987654321", Now.AddMinutes(2)).Value;
        Assert.Equal("notifications.code_expired", endpoint.Confirm(code, Now.AddMinutes(13)).Error.Code);

        code = endpoint.StartVerification("51987654321", Now.AddMinutes(14)).Value;
        Assert.True(endpoint.Confirm($" {code} ", Now.AddMinutes(15)).IsSuccess);
        Assert.Equal(("51987654321", null), (endpoint.Phone, endpoint.PendingPhone));
    }

    [Fact]
    public void An_event_from_whatsapp_gives_its_text_messages_and_delivery_reports_and_skips_the_rest()
    {
        const string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
              "messages":[
                {"from":"51987654321","id":"wamid.A","type":"text","text":{"body":"saldo"}},
                {"from":"51987654321","id":"wamid.B","type":"image","image":{"id":"9"}}],
              "statuses":[
                {"id":"wamid.OUT","status":"failed","errors":[{"code":131047,"title":"Re-engagement message"}]},
                {"id":"wamid.OUT","status":"deleted"}]}}]}]}
            """;

        var payload = WhatsAppPayload.Parse(body);

        Assert.Equal([new WhatsAppMessage("wamid.A", "51987654321", "saldo")], payload.Messages);
        Assert.Equal([new WhatsAppStatusReport("wamid.OUT", DeliveryStatus.Failed, "Re-engagement message")], payload.Statuses);
    }

    [Theory]
    [InlineData(" Saldo ", ChatCommand.Balance)]
    [InlineData("¿ayuda?", ChatCommand.Help)]
    [InlineData("gasté 20 en taxi", null)]
    public void Only_balance_and_help_are_commands(string text, ChatCommand? expected) => Assert.Equal(expected, ChatCommandParser.Parse(text));
}
