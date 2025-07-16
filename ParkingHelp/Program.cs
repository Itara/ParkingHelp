using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.Playwright;
using ParkingHelp.DB;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;
using System.Diagnostics;
using System.Text.Json.Serialization; // DbContext 네임스페이스


var builder = WebApplication.CreateBuilder(args);
//var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var filePath = Path.Combine(Directory.GetCurrentDirectory(), "DB.json");
var config = builder.Configuration;

if (!File.Exists(filePath))
{
    Console.WriteLine("⚠️ DB.json 파일을 찾을 수 없습니다. 기본 설정 또는 환경변수를 사용합니다.");
    builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    builder.Configuration.AddJsonFile("DB.json", optional: true, reloadOnChange: true);
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
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ParkingHelp v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.MapControllers();
/////
/*
_= Task.Run(async () =>
{
    using var playwright = await Playwright.CreateAsync();
    try
    {
        var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            SlowMo = 100
        });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        Console.WriteLine("로그인 페이지 이동 중...");
        await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new PageGotoOptions
        {
            Timeout = 60000
        });

        // 입력 대기 후 아이디, 비밀번호 채우기
        await page.WaitForSelectorAsync("#id");
        await page.FillAsync("#id", "C2115");
        await page.FillAsync("#password", "6636");

        // 로그인 버튼 클릭
        await page.ClickAsync("#loginBtn");

        // 로그인 후 URL 또는 특정 요소 대기 (필요시 수정)
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Console.WriteLine(" 로그인 완료! .");
        // 1. 차량번호 텍스트박스 입력
        await page.FillAsync("#carNo", "0929");
        await page.ClickAsync("#btnCarSearch");
        await page.WaitForSelectorAsync("#searchDataTable tbody tr");

        // 4. 첫 번째 행에서 차량번호와 입차시간 추출
        var row = await page.QuerySelectorAsync("#searchDataTable tbody tr");
        if (row != null)
        {
            var carNoSpans = await page.Locator("table#searchDataTable span").AllInnerTextsAsync();
            string carNum = "";
            foreach (var carNo in carNoSpans)
            {
                Console.WriteLine($"차량번호: {carNo}");
                carNum = carNo;
                break;
            }
            if(!string.IsNullOrEmpty(carNum))
            {
                await page.WaitForSelectorAsync($"a:has-text('{carNum}')");
                await page.ClickAsync($"a:has-text('{carNum}')");

                var feeElement = page.Locator("#realFee");

                // 2. value 추출
                string feeValueRaw = await feeElement.InputValueAsync(); // 예: "0 원"

                // 3. 숫자만 추출 (공백, 원 제거)
                string numericPart = System.Text.RegularExpressions.Regex.Replace(feeValueRaw, @"[^0-9]", "");
                int feeValue = int.Parse(numericPart);

                // 4. 주차금액이 0보다 크면 방문자주차권 버튼 클릭
                if (feeValue > 0)
                {
                    Console.WriteLine($"주차금액: {feeValue}원, 방문자 주차권 클릭 시도");

                    // 버튼 id는 'add-discount-0' 으로 보임
                    await page.ClickAsync("#add-discount-0");
                }
                else
                {
                    Console.WriteLine("주차금액이 0원이므로 할인권 적용 생략");
                }

            }
        }
        Console.ReadLine();
    }
    catch(Exception ex)
    {
        Debug.WriteLine($"{ex.Message}");
    }
});
*/

app.Run();
