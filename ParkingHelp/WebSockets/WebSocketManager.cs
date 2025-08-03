using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ParkingHelp.WebSockets
{
    public static class WebSocketManager
    {
        private static readonly ConcurrentDictionary<int, WebSocketUser> _users = new();
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

        public static void AddUser(int userId, WebSocket socket)
        {
            _users[userId] = new WebSocketUser
            {
                UserId = userId,
                Socket = socket,
                ConnectedAt = DateTime.UtcNow
            };
            Console.WriteLine($"[접속] {userId}");
        }

        public static void RemoveUser(int userId)
        {
            if (_users.TryRemove(userId, out _))
            {
                Console.WriteLine($"[해제] {userId}");
            }
        }

        public static WebSocket? GetSocket(int userId)
        {
            return _users.TryGetValue(userId, out var user) ? user.Socket : null;
        }

        public static List<int> GetConnectedUserIds()
        {
            return _users.Keys.ToList();
        }

        public static IEnumerable<WebSocketUser> GetAllUsers()
        {
            return _users.Values;
        }

        public static async Task<bool> SendToUserAsync(int userId, JObject sendClientMsg)
        {
            if (_users.TryGetValue(userId, out var user) && user.Socket.State == WebSocketState.Open)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(sendClientMsg);
                var bytes = Encoding.UTF8.GetBytes(json);

                try
                {
                    await user.Socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    return true;
                }
                catch (WebSocketException ex)
                {
                    Console.WriteLine($"[{userId}] 전송 실패: {ex.Message}");
                    RemoveUser(userId); //접속오류날경우 사용자 리스트에서 제거
                }
            }
            else if (user != null && user.Socket.State != WebSocketState.Open)
            {
                RemoveUser(userId); // 정리
            }
            return false;
        }

        public static async Task<bool> SendToUserAsync(List<int> userIds, JObject sendClientMsg)
        {
            bool bResult = false;
            foreach(int userId in userIds)
            {
                if (_users.TryGetValue(userId, out var user) && user.Socket.State == WebSocketState.Open)
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(sendClientMsg);
                    var bytes = Encoding.UTF8.GetBytes(json);

                    try
                    {
                        await user.Socket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                        bResult = true;
                    }
                    catch (WebSocketException ex)
                    {
                        Console.WriteLine($"[{userId}] 전송 실패: {ex.Message}");
                        RemoveUser(userId); //접속오류날경우 사용자 리스트에서 제거
                        bResult =false;
                    }
                }
                else if (user != null && user.Socket.State != WebSocketState.Open)
                {
                    RemoveUser(userId); // 정리
                    bResult = false;
                }
            }
            return bResult;
        }

        public static async Task StartPingLoopAsync()
        {
            while (true)
            {
                foreach (var user in GetAllUsers().ToList())
                {
                    if (user.Socket.State != WebSocketState.Open)
                    {
                        RemoveUser(user.UserId);
                        continue;
                    }

                    var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                    try
                    {
                        await user.Socket.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch
                    {
                        Console.WriteLine($"[{user.UserId}] ping 실패, 제거됨");
                        RemoveUser(user.UserId);
                    }
                }

                await Task.Delay(PingInterval);
            }
        }


    }
}
