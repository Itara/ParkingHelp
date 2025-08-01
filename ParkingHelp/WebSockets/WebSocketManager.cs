using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ParkingHelp.WebSockets
{
    public static class WebSocketManager
    {
        private static readonly ConcurrentDictionary<string, WebSocketUser> _users = new();
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

        public static void AddUser(string userId, WebSocket socket)
        {
            _users[userId] = new WebSocketUser
            {
                UserId = userId,
                Socket = socket,
                ConnectedAt = DateTime.UtcNow
            };
            Console.WriteLine($"[접속] {userId}");
        }

        public static void RemoveUser(string userId)
        {
            if (_users.TryRemove(userId, out _))
            {
                Console.WriteLine($"[해제] {userId}");
            }
        }

        public static WebSocket? GetSocket(string userId)
        {
            return _users.TryGetValue(userId, out var user) ? user.Socket : null;
        }

        public static List<string> GetConnectedUserIds()
        {
            return _users.Keys.ToList();
        }

        public static IEnumerable<WebSocketUser> GetAllUsers()
        {
            return _users.Values;
        }

        public static async Task<bool> SendToUserAsync(string userId, JObject sendClientMsg)
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
                    RemoveUser(userId); // 정리
                }
            }
            else if(user.Socket.State != WebSocketState.Open)
            {
                RemoveUser(userId); // 정리
            }

                return false;
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
