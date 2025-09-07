using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Microsoft.Playwright;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.Logging;
using ParkingHelp.Models;
using ParkingHelp.ParkingDiscountBot;
using ParkingHelp.SlackBot;
using ParkingHelp.WebSockets;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization; // DbContext 네임스페이스
using ParkingHelp.Services.ParkingDiscount;

var builder = WebApplication.CreateBuilder(args);
//var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Setting.json");
var config = builder.Configuration;

if (!File.Exists(filePath))
{
    Console.WriteLine("Setting.json 파일을 찾을 수 없습니다. 기본 설정 또는 환경변수를 사용합니다.");
    builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")).LogTo(Console.WriteLine, LogLevel.Information));

    //Slack Token 설정
    var slackToken = config["Slack:BotToken"]; // 환경변수: Slack__BotToken
    var slackChannel = config["SlackChannelId"]; 
    Console.WriteLine($"slackToken Get : {slackToken}");
    builder.Services.AddSingleton(new SlackOptions
    {
        BotToken = slackToken ?? "",
        ChannelId = slackChannel ?? ""
    });
}
else
{
    builder.Configuration.AddJsonFile("Setting.json", optional: true, reloadOnChange: true);
    builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    //Slack Token 설정
    var slackToken = config["Slack:Token"];
    var slackChannel = config["SlackChannelId"];
    Console.WriteLine($"slackToken Get : {slackToken}");
    builder.Services.AddSingleton(new SlackOptions
    {
        BotToken = slackToken ?? "",
        ChannelId = slackChannel ?? ""
    });
}
// Add services to the container.
Console.WriteLine($"Connection String is : {builder.Configuration.GetConnectionString("DefaultConnection")}");

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Parking Help",
        Version = "v1",
        Description = "Pharmsoft ParkingHelp Rest API"
    });

    c.EnableAnnotations();
    
    // XML 문서 파일 경로 설정
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddScoped<ParkingAutomation>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ParkingHelp v1");
    c.RoutePrefix = "swagger";
});
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(60), // Ping 없이도 기본 연결 유지
});

var wsHandler = new WebSocketHandler();
app.Map("/ws", wsHandler.HandleAsync);
app.MapControllers();
_ = Task.Run(() => ParkingHelp.WebSockets.WebSocketManager.StartPingLoopAsync());

//builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);

// log4net 설정
var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
Logs.Init(builder.Configuration);
string logDirectory = "Logs";
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}
Logs.Info($"Log Directory: {logDirectory}");
ParkingDiscountManager.Initialize(app.Services,builder.Configuration);
Logs.Info("Parking Helper Start...");

app.Run();
