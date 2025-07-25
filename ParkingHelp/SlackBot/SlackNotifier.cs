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
        public long ? SlackThreadTs { get; set; } = null; // 슬랙 스레드 타임스탬프 (고유  ID)
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message">전달할 메세지</param>
        /// <param name="userId">사용자 고유 SlackID</param>
        /// <param name="slackThreadTs">null일시 채팅 값이있으면 쓰레드 댓글</param>
        /// <returns></returns>
        public async Task<JObject> SendMessageAsync(string message,string userId,string? slackThreadTs = null)
        {
           
            JObject request  = new JObject
            {
                ["channel"] = _sendChannelID,
                ["text"] = message,
                ["link_names"] = true,
                
            };
            if (!string.IsNullOrEmpty(slackThreadTs))
            {
                request .Add("thread_ts", slackThreadTs);
            }
            Console.WriteLine($"BotToken : {_botToken} , channel : {request["channel"]} {request["message"]}");
            var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://slack.com/api/chat.postMessage", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("슬랙 메시지 전송 실패: " + response.StatusCode);
                return JObject.Parse(responseText);
            }
            else
            {
                JObject responseJobj = JObject.Parse(responseText);
                if (!Convert.ToBoolean(responseJobj["ok"]))
                {
                    Console.WriteLine("슬랙 메시지 전송 실패");
                    return JObject.Parse(responseText);
                }
                else
                {
                    Console.WriteLine ("슬랙 메시지 전송 성공!");
                    return JObject.Parse(responseText);
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

            Console.WriteLine("사용자 조회 실패: " + obj["error"]);
            return null;
        }
    }

}
