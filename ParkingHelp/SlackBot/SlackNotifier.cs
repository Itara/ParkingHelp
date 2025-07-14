using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ParkingHelp.SlackBot
{
    public class SlackUserByEmail
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class SlackOptions
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChannelId { get; set; } = "C04T9K8F5G1"; // 기본 채널 설정, 필요시 변경 가능
    }
    public class SlackNotifier
    {
        private readonly string _botToken;
        private readonly string _sendChannelID = string.Empty;
        private readonly HttpClient _httpClient;
        
        public SlackNotifier(SlackOptions options)
        {
            _botToken = options.BotToken;
            _sendChannelID = options.ChannelId; // 기본 채널 설정, 필요시 변경 가능
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _botToken);
        }

        public async Task SendMessageAsync(string message)
        {
            var payload = new
            {
                channel = _sendChannelID,  // "#general" 또는 채널 ID ("C01XXXXXX")
                text = message,
                link_names = true
            };

            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://slack.com/api/chat.postMessage", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("슬랙 메시지 전송 실패: " + response.StatusCode);
                Console.WriteLine(responseText);
            }
            else
            {
                JObject responseJobj = JObject.Parse(responseText);
                if (!Convert.ToBoolean(responseJobj["ok"]))
                {
                    Console.WriteLine("슬랙 메시지 전송 실패");
                    Console.WriteLine($"{responseText}");
                }
                else
                {
                    Console.WriteLine("슬랙 메시지 전송 성공!");
                }
            }
        }

        public async Task<SlackUserByEmail?> FindUserByEmailAsync(string email)
        {
            var res = await _httpClient.GetAsync($"https://slack.com/api/users.lookupByEmail?email={email}");
            var json = await res.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);

            if (obj["ok"]?.Value<bool>() == true)
            {
                var user = obj["user"];
                return new SlackUserByEmail
                {
                    Id = user?["id"]?.ToString() ?? "",
                    Name = user?["real_name"]?.ToString() ?? "",
                    Email = user?["profile"]?["email"]?.ToString() ?? ""
                };
            }

            Console.WriteLine("❌ 사용자 조회 실패: " + obj["error"]);
            return null;
        }
    }

}
