using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace WebApp.Server
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _channels = new();

        public async Task JoinChannel(string channelName, string userName)
        {
            var clients = _channels.GetOrAdd(channelName, _ => new ConcurrentDictionary<string, string>());
            clients.TryAdd(Context.ConnectionId, userName);

            await Groups.AddToGroupAsync(Context.ConnectionId, channelName);
            
            var timestamp = DateTime.Now.ToString("HH:mm");
            await Clients.Group(channelName).SendAsync("ReceiveMessage", $"{timestamp} : {userName} kanala katıldı");
        }

        public async Task SendMessage(string channelName, string userName, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm");
            await Clients.Group(channelName).SendAsync("ReceiveMessage", $"{timestamp} : {userName} :> {message}");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var channel in _channels)
            {
                if (channel.Value.TryRemove(Context.ConnectionId, out var userName))
                {
                    var timestamp = DateTime.Now.ToString("HH:mm");
                    await Clients.Group(channel.Key).SendAsync("ReceiveMessage", $"{timestamp} : {userName} kanaldan ayrıldı");
                    
                    if (channel.Value.IsEmpty)
                    {
                        _channels.TryRemove(channel.Key, out _);
                    }
                    
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel.Key);
                }
            }
            
            await base.OnDisconnectedAsync(exception);
        }
    }
} 