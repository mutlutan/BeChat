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

app.MapGet("/", (IServiceProvider serviceProvider, HttpContext context) =>
{
	var webHostEnvironment = serviceProvider.GetService<IWebHostEnvironment>();
	string dosya = System.IO.File.ReadAllText((webHostEnvironment?.WebRootPath ?? "") + "\\index.html");

	dosya = dosya.Replace("wss://localhost:44334/chat/", "wss://"+ context.Request.Host.ToString() + "/chat/");

	return Results.Content(dosya, Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html"));
}).ExcludeFromDescription();

// Kanal bazlý baðlý istemcileri saklayan koleksiyon
var channels = new ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>>();

app.UseWebSockets();
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
	while (webSocket.State == WebSocketState.Open)
	{
		var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
		if (result.MessageType == WebSocketMessageType.Close)
		{
			if (channel != null && channels.TryGetValue(channel, out var clients))
			{
				clients.TryRemove(clientId, out _);
			}
			await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
			break;
		}
		else
		{
			var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
			var messageObj = JsonSerializer.Deserialize<ChatMessage>(messageJson);
			if (messageObj != null)
			{
				if (messageObj.Type == "join")
				{
					channel = messageObj.Channel;
					var clients = channels.GetOrAdd(channel, _ => new ConcurrentDictionary<string, WebSocket>());
					clients.TryAdd(clientId, webSocket);
				}
				else if (messageObj.Type == "message" && channel != null && channels.TryGetValue(channel, out var clients))
				{
					var fullMessage = $"{clientId}: {messageObj.Content}";
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


app.Run();

record ChatMessage(string Type, string Channel, string Content);
