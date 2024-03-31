namespace DiscordTurbineChat
{
    public enum DiscordChatChannel
    {
        Audit = 1,
        General = 2
    }

    public class ChatMessagePayload
    {
        public string content { get; set; }
    }

    public static class WebhookRepository
    {
        public static async Task SendWebhookChat(DiscordChatChannel channel, String message, string webhookUrl)
        {
            await Task.Run(async () =>
            {
                using (var httpClient = new HttpClient())
                {

                    var payload = new ChatMessagePayload
                    {
                        content = message
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);

                    var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await httpClient.PostAsync(webhookUrl, httpContent);

                    response.EnsureSuccessStatusCode();
                }
            });
        }
    }
}
