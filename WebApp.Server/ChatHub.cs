using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
            await Clients.Group(channelName).SendAsync("ReceiveMessage", $"{timestamp} : {userName} kanala katıldı", "", "system");
            
            // Kullanıcı listesini güncelle
            await SendUserList(channelName);
        }

        public async Task SendMessage(string channelName, string userName, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm");
            var formattedMessage = $"{timestamp} : {userName} :> {message}";
            
            // Mesajı gönderen kullanıcıya, kendi ID'si ile birlikte gönderiyoruz
            await Clients.Caller.SendAsync("ReceiveMessage", formattedMessage, Context.ConnectionId, "self");
            
            // Diğer kullanıcılara, mesajı gönderenin ID'si ile gönderiyoruz
            await Clients.OthersInGroup(channelName).SendAsync("ReceiveMessage", formattedMessage, Context.ConnectionId, "other");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var channel in _channels)
            {
                if (channel.Value.TryRemove(Context.ConnectionId, out var userName))
                {
                    var timestamp = DateTime.Now.ToString("HH:mm");
                    await Clients.Group(channel.Key).SendAsync("ReceiveMessage", $"{timestamp} : {userName} kanaldan ayrıldı", "", "system");
                    
                    if (channel.Value.IsEmpty)
                    {
                        _channels.TryRemove(channel.Key, out _);
                    }
                    else
                    {
                        // Kullanıcı listesini güncelle
                        await SendUserList(channel.Key);
                    }
                    
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel.Key);
                }
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        // Kullanıcı listesini ilgili kanala gönder
        private Task SendUserList(string channelName)
        {
            if (_channels.TryGetValue(channelName, out var users))
            {
                var userList = users.Values.Distinct().OrderBy(x => x).ToList();
                return Clients.Group(channelName).SendAsync("UserList", userList);
            }
            return Task.CompletedTask;
        }
    }
} 