using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using WebApp.Server;

var builder = WebApplication.CreateBuilder(args);

// SignalR servislerini ekliyoruz
builder.Services.AddSignalR();

// CORS politikalarını düzeltiyoruz - wildcard (*) ve credentials birlikte kullanılamaz
builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(builder =>
	{
		builder.AllowAnyOrigin()
			   .AllowAnyHeader()
			   .AllowAnyMethod();
	
	});
});

var app = builder.Build();

// CORS'u etkinleştiriyoruz
app.UseCors();

// wwwroot klasöründeki statik dosyaları servis etmek için
app.UseStaticFiles();

// Ana sayfa için özel yönlendirme
app.MapGet("/", (IServiceProvider serviceProvider, HttpContext context) =>
{
	var webHostEnvironment = serviceProvider.GetService<IWebHostEnvironment>();
	string dosya = System.IO.File.ReadAllText((webHostEnvironment?.WebRootPath ?? "") + "\\index.html");
	
	// WebSocket yerine SignalR hub URL'sini kullanıyoruz
	var host = context.Request.Host.ToString();
	var scheme = context.Request.Scheme;
	dosya = dosya.Replace("___HOST_URL___", $"{scheme}://{host}");
	
	return Results.Content(dosya, Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html"));
}).ExcludeFromDescription();

// ChatHub'ı /chathub endpoint'ine yönlendiriyoruz
app.MapHub<ChatHub>("/chathub");

app.Run();
