using Newtonsoft.Json.Linq;
using ParkingHelp.Logging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
namespace ParkingHelp.WebSockets
{
    /// <summary>
    /// 웹소켓 클라이언트부터 받은 수신데이터 처리
    /// </summary>
    public class WebSocketHandler
    {
        private readonly Dictionary<string, Func<Task<JObject>>> _handlers; //Protocol정의

        public WebSocketHandler()
        {
            _handlers = new Dictionary<string, Func<Task<JObject>>>()
            {
                ["ping"] = PingCheck,
                ["HelpOfferRegist"] = ProcessHelpOfferNewRegist, 
                ["HelpOfferUpdate"] = ProcessHelpOfferUpdate,
                ["HelpOfferDelete"] = ProcessHelpOfferDelete,
                ["ReqHelpRegist"] = ProcessRequestHelpNewRegist,
                ["ReqHelpUpdate"] = ProcessRequestHelpUpdate,
                ["ReqHelpDelete"] = ProcessRequestHelpDelete,
                ["ReqHelpDetailDelete"] = ProcessRequestHelpDetailDelete
            };
        }

        public async Task HandleAsync(HttpContext context)
        {
            // 쿼리에서 userId 받기
            int userId = 0;
            string getQueryUserID = context.Request.Query["userId"].ToString();

            if (string.IsNullOrWhiteSpace(getQueryUserID))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Missing userId");
                return;
            }
            userId = Convert.ToInt32(getQueryUserID);
            // 웹소켓 연결 수락
            WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            WebSocketManager.AddUser(userId, socket);

            byte[] buffer = new byte[1024 * 8];
            
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.Count > buffer.Length)
                    {
                        Logs.Info($"[{userId}] 너무 큰 메시지, 연결 종료");
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                        WebSocketManager.RemoveUser(userId);
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string? message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Logs.Info($"[{userId}] 메시지 수신: {message}");

                        try
                        {
                            JObject jobj = JObject.Parse(message); // { "type" : _handlers<Key> , "payload" : {실수신데이터...} }
                            string protocol = jobj["type"]?.ToString() ?? ""; //브라우저로부터 수신받은 Protocol : _handlers의 Key값으로 받는다
                            JObject? payload = jobj["payload"] as JObject;

                            if (payload != null) //실 수신 데이터
                            {
                                if (!string.IsNullOrEmpty(protocol) && _handlers.TryGetValue(protocol, out var handler))
                                {
                                    var response = await handler(); //_handlers의 Value값의 함수를 실행
                                    if (response != null)
                                    {
                                        var responseBytes = Encoding.UTF8.GetBytes(response.ToString());
                                        await socket.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                }
                                else
                                {
                                    Logs.Info($"[경고] 알 수 없는 protocol: {protocol}");
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            Logs.Error($"[오류] JSON 파싱 실패: {ex.Message}",ex);
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            finally
            {
                WebSocketManager.RemoveUser(userId);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                Logs.Info($"[{userId}] 연결 종료");
            }
        }

        public async Task<JObject> PingCheck()
        {
            JObject returnJOb = new JObject();
            Console.WriteLine("Get Ping...");
            return returnJOb;
        }

        public async Task<JObject> ProcessHelpOfferNewRegist()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }

        public async Task<JObject> ProcessHelpOfferUpdate()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }
        public async Task<JObject> ProcessHelpOfferDelete()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }

        public async Task<JObject> ProcessRequestHelpNewRegist()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }
        public async Task<JObject> ProcessRequestHelpUpdate()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }
        public async Task<JObject> ProcessRequestHelpDelete()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }
        public async Task<JObject> ProcessRequestHelpDetailDelete()
        {
            JObject returnJOb = new JObject();
            return returnJOb;
        }
    }
}
