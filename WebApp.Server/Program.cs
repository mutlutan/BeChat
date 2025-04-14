using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// sürekli soket bağlantısı kopuyor

app.MapGet("/", (IServiceProvider serviceProvider, HttpContext context) =>
{
	var webHostEnvironment = serviceProvider.GetService<IWebHostEnvironment>();
	string dosya = System.IO.File.ReadAllText((webHostEnvironment?.WebRootPath ?? "") + "\\index.html");

	dosya = dosya.Replace("wss://localhost:44334/chat/", "wss://"+ context.Request.Host.ToString() + "/chat/");

	return Results.Content(dosya, Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html"));
}).ExcludeFromDescription();

// Kanal bazlı bağlı istemcileri saklayan koleksiyon
var channels = new ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>>();

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(15) // 15dk düşürüldü
};
app.UseWebSockets(webSocketOptions);

app.Map("/chat/{clientId}", async (string clientId, HttpContext context) =>
{
	if (context.WebSockets.IsWebSocketRequest)
	{
		var webSocket = await context.WebSockets.AcceptWebSocketAsync();
		//var clientId = Guid.NewGuid().ToString();
		await HandleWebSocketAsync(clientId, webSocket);
	}
	else
	{
		context.Response.StatusCode = 400;
	}
});

async Task HandleWebSocketAsync(string clientId, WebSocket webSocket)
{
	string? channel = null;
	var buffer = new byte[1024 * 4];
	var pingTimer = new Timer(async _ =>
	{
		if (webSocket.State == WebSocketState.Open)
		{
			try
			{
				// Ping mesajı gönder
				await webSocket.SendAsync(
					new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")),
					WebSocketMessageType.Text,
					true,
					CancellationToken.None);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ping error for {clientId}: {ex.Message}");
			}
		}
	}, null, TimeSpan.Zero, TimeSpan.FromSeconds(20)); // Her 20 saniyede bir ping

	try
	{
		while (webSocket.State == WebSocketState.Open)
		{
			var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
			
			if (result.MessageType == WebSocketMessageType.Close)
			{
				if (channel != null && channels.TryGetValue(channel, out var clients))
				{
					clients.TryRemove(clientId, out _);
					
					// Kullanıcı kanaldan ayrıldığında diğer kullanıcılara bildirim gönder
					var timestamp = DateTime.Now.ToString("HH:mm");
					var disconnectMessage = $"{timestamp} : {clientId} kanaldan ayrıldı";
					var disconnectMessageBytes = Encoding.UTF8.GetBytes(disconnectMessage);
					
					foreach (var client in clients.Values)
					{
						if (client.State == WebSocketState.Open)
						{
							await client.SendAsync(new ArraySegment<byte>(disconnectMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
						}
					}
					
					// Eğer kanalda kimse kalmadıysa kanalı kaldır
					if (clients.IsEmpty)
					{
						channels.TryRemove(channel, out _);
					}
				}
				await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
				break;
			}
			else
			{
				var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
				
				// Ping mesajına yanıt ver
				if (messageJson == "ping")
				{
					await webSocket.SendAsync(
						new ArraySegment<byte>(Encoding.UTF8.GetBytes("pong")),
						WebSocketMessageType.Text,
						true,
						CancellationToken.None);
					continue;
				}
				else if (messageJson == "pong")
				{
					// Pong alındı, bağlantı sağlam
					continue;
				}

				var messageObj = JsonSerializer.Deserialize<ChatMessage>(messageJson);
				if (messageObj != null)
				{
					if (messageObj.Type == "join")
					{
						channel = messageObj.Channel;
						var clients = channels.GetOrAdd(channel, _ => new ConcurrentDictionary<string, WebSocket>());
						clients.TryAdd(clientId, webSocket);
						
						// Kullanıcı kanala katıldığında kendisine de bildirim gönder
						var timestamp = DateTime.Now.ToString("HH:mm");
						var joinMessage = $"{timestamp} : {clientId} kanala katıldı";
						var joinMessageBytes = Encoding.UTF8.GetBytes(joinMessage);
						
						foreach (var kvp in clients)
						{
							if (kvp.Value.State == WebSocketState.Open) // Tüm kullanıcılara, kendisi dahil
							{
								await kvp.Value.SendAsync(new ArraySegment<byte>(joinMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
							}
						}
					}
					else if (messageObj.Type == "message" && channel != null && channels.TryGetValue(channel, out var clients))
					{
						var timestamp = DateTime.Now.ToString("HH:mm");
						var fullMessage = $"{timestamp} : {clientId} :> {messageObj.Content}";
						var messageBytes = Encoding.UTF8.GetBytes(fullMessage);
						foreach (WebSocket client in clients.Values)
						{
							if (client.State == WebSocketState.Open)
							{
								await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
							}
						}
					}
				}
			}
		}
	}
	catch (Exception ex)
	{
		Console.WriteLine($"WebSocket error: {ex.Message}");
	}
	finally
	{
		pingTimer.Dispose();
		// Kullanıcı bağlantısı koptuğunda
		if (channel != null && channels.TryGetValue(channel, out var channelClients))
		{
			// Kullanıcıyı kanaldan çıkar
			channelClients.TryRemove(clientId, out _);
			
			// Diğer kullanıcılara bildirim gönder
			var timestamp = DateTime.Now.ToString("HH:mm");
			var disconnectMessage = $"{timestamp} : {clientId} kanaldan ayrıldı";
			var disconnectMessageBytes = Encoding.UTF8.GetBytes(disconnectMessage);
			
			foreach (var client in channelClients.Values)
			{
				if (client.State == WebSocketState.Open)
				{
					await client.SendAsync(new ArraySegment<byte>(disconnectMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
				}
			}
			
			// Eğer kanalda kimse kalmadıysa kanalı kaldır
			if (channelClients.IsEmpty)
			{
				channels.TryRemove(channel, out _);
			}
		}
	}
}


app.Run();

record ChatMessage(string Type, string Channel, string Content);
