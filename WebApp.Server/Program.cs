using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using WebApp.Server;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// SignalR servislerini ekliyoruz ve yapılandırıyoruz
builder.Services.AddSignalR(options => 
{
	// Bağlantı zaman aşımını artır
	options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
	options.KeepAliveInterval = TimeSpan.FromMinutes(1);
	
	// Deploy ortamında daha ayrıntılı günlük kaydı
	options.EnableDetailedErrors = true;
});

// CORS politikalarını düzeltiyoruz - deploy ortamı için
builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy =>
	{
		policy.SetIsOriginAllowed(_ => true) // Tüm origin'lere izin ver
			   .AllowAnyHeader()
			   .AllowAnyMethod()
			   .AllowCredentials(); // SignalR için gerekli
	});
});

var app = builder.Build();

// CORS'u SignalR'den önce etkinleştiriyoruz
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
	
	// Bağlantı URL'sini oluştur - HTTP/HTTPS farkını dikkate alarak
	string connectionUrl;
	if (app.Environment.IsDevelopment())
	{
		// Geliştirme ortamında mevcut scheme'i kullan
		connectionUrl = $"{scheme}://{host}";
	}
	else
	{
		// Production ortamında her zaman wss kullan (https demek)
		connectionUrl = $"https://{host}";
	}
	
	// URL'yi index.html içine yerleştir
	dosya = dosya.Replace("___HOST_URL___", connectionUrl);
	
	return Results.Content(dosya, Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html"));
}).ExcludeFromDescription();

// ChatHub'ı /chathub endpoint'ine yönlendiriyoruz
app.MapHub<ChatHub>("/chathub");

app.Run();
