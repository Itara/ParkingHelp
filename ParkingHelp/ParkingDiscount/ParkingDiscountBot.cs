using log4net;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.Common;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.Logging;
using ParkingHelp.Models;
using ParkingHelp.ParkingDiscount;
using ParkingHelp.SlackBot;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Channels;
namespace ParkingHelp.ParkingDiscountBot
{

    public class ParkingDiscountResultEventArgs : EventArgs
    {
        public string CarNumber { get; set; }
        public JObject Result { get; set; }
        public string MemberEmail { get; set; } = string.Empty;
        public bool IsNotifySlack { get; set; } = false; //슬랙에 알림 여부
        public ParkingDiscountResultEventArgs(string carNumber, string memberEmail, JObject result, bool isNotifySlack = false)
        {
            CarNumber = carNumber;
            Result = result;
            MemberEmail = memberEmail;
            IsNotifySlack = isNotifySlack;
        }
    }

    /// <summary>
    /// 방문자 주차권 할인권 등록 관리 클래스
    /// </summary>
    public static class ParkingDiscountManager
    {
        //주차할인권 적용 페이지에 접근할 수 있는 Playwright 객체
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
        private static IBrowserContext? _context;  // 전역 context 추가
        //작업큐 관련
        private static PriorityQueue<(ParkingDiscountModel, DiscountJobType jobType, TaskCompletionSource<JObject> tcs), int> _ParkingDiscountPriorityQueue = new();
        private static SemaphoreSlim _semaphore = new(0);
        private static object _lock = new();

        private static TimeOnly _AutoDisCountApplyTime = new TimeOnly(17, 0, 0); // 자동 할인권 적용 시간 (17시 기준)
        //설정 및 의존성 주입 객체
        private static IConfiguration? _config;
        private static IServiceProvider? _services;

        private static AppDbContext _DbContext = null; // DB Context
                                                       //슬랙 채널에 결과 알림
        private static SlackOptions slackOptions = null;
        private static SlackNotifier slackNotifier = null;


        private static List<ParkingAccount> ParkingAccounts = new();

        /// <summary>
        /// 시간 블록당 요금 (예: TIME_BLOCK_MINUTES당 2000원)
        /// </summary>
        public const int FEE_PER_TIME_BLOCK = 2000;
        /// <summary>
        /// 요금이 적용되는 시간 블록 단위 
        /// </summary>
        public const int TIME_BLOCK_MINUTES = 30;
        /// <summary>
        /// 요금 정산 후 출차까지 보장시간
        /// </summary>
        public const int BUFFER_OUT_TIME_MINUTES = 15;

        public const int FREE_PARKING_MINUTES = 30; //무료 주차 시간 (15분)
        public static readonly TimeOnly GET_OFF_WORK_TIME = new TimeOnly(18, 0, 0);

        //퇴근시간 버퍼 시간 (10분)
        public static event EventHandler<ParkingDiscountResultEventArgs>? OnParkingDiscountEvent; //주차 결과 이벤트
        public static string SessionFilePath = Path.Combine(AppContext.BaseDirectory, "session.json");
        public static void Initialize(IServiceProvider services, IConfiguration config)
        {
            //DI주입
            _services = services;
            _config = config;
            //Slack 설정
            slackOptions = _services.GetRequiredService<SlackOptions>();
            slackNotifier = new SlackNotifier(slackOptions);
            ParkingAccounts = _config.GetSection("ParkingAccounts").Get<List<ParkingAccount>>().OrderBy(a => a.IsMain).ToList() ?? new(); // false → true 순서
            //주차등록 결과 이벤트
            OnParkingDiscountEvent += PlaywrightManager_OnParkingDiscountEvent;

            //주차할인권 등록리스트를 받을 비동기 작업 시작

            _ = Task.Run(async () =>
            {
                _playwright = await Playwright.CreateAsync();
                //_browser = await _playwright.Chromium.LaunchAsync(new()
                //{
                //    Headless = true,
                //    Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage" }
                //});
#if DEBUG
                _browser = await _playwright.Chromium.LaunchAsync(new()
                {
                    Headless = false,
                    Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage" }
                });
#elif RELEASE
                _browser = await _playwright.Chromium.LaunchAsync(new()
                {
                    Headless = true,
                    Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage" }
                });
#endif

                _context = await _browser.NewContextAsync();  // 이걸 tempContext로 안하고 바로 _context로 설정
                var page = await _context.NewPageAsync();

                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                await page.FillAsync("#id", "C2115");
                await page.FillAsync("#password", "6636");
                await page.ClickAsync("#loginBtn");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                bool isOnlyFirstRun = true; //즉시 실행이면 한번만 실행한다 
                string? autoDiscountTime = _config["AutoDiscountTime"];
                string? familyDayTime = _config["FamilyDayTime"];
                DateTime? lastRunTime = null;

                Logs.Info($"자동 할인권 적용 시간 {autoDiscountTime ?? ""}");
                Logs.Info($"Family Day 적용 시간 {familyDayTime ?? ""}");
                while (true)
                {
                    DateTime currentTime = DateTime.Now;
                    DateTime currentMinute = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day,
                                     currentTime.Hour, currentTime.Minute, 0);

                    #region 주차자동등록
                    //즉시 실행 1번만 실행
                    if (_config["AutoDiscountTime"] != null
                        && _config["AutoDiscountTime"].Equals("NOW", StringComparison.CurrentCultureIgnoreCase)
                        && isOnlyFirstRun)
                    {
                        Logs.Info("할인권 즉시 적용 상태입니다. 사용자 조회를 시작합니다.");
                        List<MemberDto> members = GetMemberList();
                        ApplyDiscountToMembers(members);
                        isOnlyFirstRun = false;
                        Logs.Info("주차할인권 즉시 적용 완료");
                    }

                    // 금요일 FamilyDay
                    else if (DateTime.Now.DayOfWeek == DayOfWeek.Friday
                        && TimeOnly.TryParse(familyDayTime, out _AutoDisCountApplyTime)
                        && _AutoDisCountApplyTime.Hour == currentTime.Hour
                        && _AutoDisCountApplyTime.Minute == currentTime.Minute
                         && (!lastRunTime.HasValue || lastRunTime.Value != currentMinute))
                    {
                        lastRunTime = currentMinute;
                        Logs.Info($"금요일 FamilyDay 할인권 적용 시간({DateTime.Now:HH:mm:ss})입니다.");
                        List<MemberDto> members = GetMemberList();
                        if (members.Count == 0)
                        {
                            Logs.Info("사용자를 1명도 조회하지 못했습니다.");
                            await Task.Delay(1000);
                            lastRunTime = null;
                            continue;
                        }
                        ApplyDiscountToMembers(members);
                    }
                    //평일 퇴근시간
                    else if (DateTime.Now.DayOfWeek != DayOfWeek.Friday
                        && TimeOnly.TryParse(autoDiscountTime, out _AutoDisCountApplyTime)
                        && _AutoDisCountApplyTime.Hour == currentTime.Hour
                        && _AutoDisCountApplyTime.Minute == currentTime.Minute
                        && (!lastRunTime.HasValue || lastRunTime.Value != currentMinute))
                    {
                        lastRunTime = currentMinute;
                        Logs.Info($"일반 할인권 적용 시간({DateTime.Now:HH:mm:ss})입니다.");
                        List<MemberDto> members = GetMemberList();
                        if (members.Count == 0)
                        {
                            Logs.Info("사용자를 1명도 조회하지 못했습니다.");
                            await Task.Delay(1000);
                            lastRunTime = null;
                            continue;
                        }
                        ApplyDiscountToMembers(members);
                    }
                    #endregion

                    if (_ParkingDiscountPriorityQueue.Count == 0)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    //큐 동기화 설정
                    await _semaphore.WaitAsync();

                    (ParkingDiscountModel ParkingDisCountModel, DiscountJobType jobType, TaskCompletionSource<JObject> tcs) item;

                    //Lock을 사용하여 작업 큐 동기화
                    lock (_lock)
                    {
                        //PriorityQueue로 생성해서 우선순위가 높은것부터 뽑아옴
                        //우선순위
                        // 1. API로 호출된 차량 (High)
                        // 2. 퇴근 등록을 한 차량 (Medium)
                        // 3. 배치 시간이 되서 작업시간이 된 차량 (Low)
                        _ParkingDiscountPriorityQueue.TryDequeue(out item, out int priority);
                        Logs.Info("남은 주차등록 Queue Count : " + _ParkingDiscountPriorityQueue.Count);
                    }

                    try
                    {
                        ParkingDiscountModel parkingDiscountModel = item.ParkingDisCountModel;
                        var result = item.jobType switch
                        {
                            DiscountJobType.ApplyDiscount => await RunDiscountAsync(parkingDiscountModel.CarNumber, parkingDiscountModel.IsGetOffWorkTime, parkingDiscountModel.IsUseDiscountTicket, parkingDiscountModel.DisCountList),
                            DiscountJobType.CheckFeeOnly => await RunCheckFeeAsync(parkingDiscountModel.CarNumber),
                            _ => new JObject { ["Result"] = "Fail", ["ReturnMessage"] = "Unknown Job Type" }
                        };
                        item.tcs.SetResult(result);
                        OnParkingDiscountEvent?.Invoke(null, new ParkingDiscountResultEventArgs(parkingDiscountModel.CarNumber, parkingDiscountModel.MemberEmail, result, parkingDiscountModel.IsNotifySlack)); //슬랙채널에 결과를 알리기 위해 이벤트 호출
                    }
                    catch (Exception ex)
                    {
                        item.tcs.SetResult(new JObject
                        {
                            ["Result"] = "Fail",
                            ["ReturnMessage"] = ex.Message
                        });
                        Logs.Info($"배치 작업중 오류 발생{ex.Message}");
                        Logs.Info($"배치 오류 StackTrace: {ex.StackTrace}");
                    }
                }
            });
        }

        private static void ApplyDiscountToMembers(List<MemberDto> members)
        {
            foreach (var member in members)
            {
                if (member.Cars != null)
                {
                    foreach (var car in member.Cars)
                    {
                        string memberEmail = member.Email ?? string.Empty;
                        int priority = 100;
                        var discountModel = new ParkingDiscountModel(car.CarNumber, memberEmail, false);
                        _ = EnqueueAsync(discountModel, DiscountJobType.ApplyDiscount, priority);
                    }
                }
            }
        }
        private static void PlaywrightManager_OnParkingDiscountEvent(object? sender, ParkingDiscountResultEventArgs e)
        {
            if (e.IsNotifySlack)
            {
                _ = Task.Run(() => SendParkingDiscountResult(e));
            }

        }

        /// <summary>
        /// 할인권 적용 결과 이벤트
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private static async Task SendParkingDiscountResult(ParkingDiscountResultEventArgs e)
        {
            string carNumber = e.CarNumber;
            Logs.Info($"차량번호: {carNumber} 할인권 적용 결과: {e.Result["Result"]} Message : {e.Result["ReturnMessage"]} ");

            if (e.IsNotifySlack)
            {
                SlackUserByEmail? userInfo = await slackNotifier.FindUserByEmailAsync(e.MemberEmail);
                string userId = userInfo?.Id ?? "U07K1ET8Q1M";
                //userId = !string.IsNullOrEmpty(userId) ? $"<@{userId}>" : string.Empty;
                switch (Convert.ToInt32(e.Result["ResultType"]))
                {
                    case (int)DisCountResultType.Success:
                        Logs.Info($"SendParkingDiscountResult() 차량번호: {carNumber} 할인권 적용 완료");
                        _ = Task.Run(() => slackNotifier.SendDMAsync($"차량번호: {carNumber} 할인권 적용 완료", userId));
                        break;
                    case (int)DisCountResultType.CarMoreThanTwo:
                        await slackNotifier.SendMessageAsync($"{userId} 차량번호: {carNumber} 할인권 적용 결과: 차량정보가 2대 이상입니다.", null);
                        break;
                    case (int)DisCountResultType.SuccessButFee:
                        Logs.Info($"SendParkingDiscountResult() 차량번호: {carNumber} 할인권 적용을 하려했지만 잔액이 존재합니다.");
                        break;
                    case (int)DisCountResultType.NoFee:
                    case (int)DisCountResultType.NotFound:

                    default:
                        break;
                }
            }
        }

        public static List<MemberDto> GetMemberList()
        {
            //db객체 가져옴
            List<MemberDto> members = new List<MemberDto>();
            try
            {
                if (_services != null)
                {
                    using var scope = _services.CreateScope();
                    _DbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    members = _DbContext.Members
                    .Include(m => m.Cars).Where(m => m.Cars != null && m.Cars.Count > 0)
                    .Select(m => new MemberDto
                    {
                        Id = m.Id,
                        MemberLoginId = m.MemberLoginId,
                        Name = m.MemberName,
                        Email = m.Email,
                        Cars = m.Cars.Select(c => new MemberCarDTO
                        {
                            Id = c.Id,
                            CarNumber = c.CarNumber
                        }).ToList()
                    }).ToList();
                }
                else
                {
                    Logs.Error("_services Is Null...");
                }
            }
            catch (Exception ex)
            {
                Logs.Info("GetMemberList() Error : " + ex.ToString());
            }
            return members;
        }

        /// <summary>
        /// 할인권 적용 작업큐에 신규 리스트 추가
        /// </summary>
        /// <param name="discountModel">주차 정산 관련 model</param>
        /// <param name="jobType">주차요금 조회 or 할인권 조회</param>
        /// <param name="priority">실행 우선순위 (0이 될수록 우선순위 증가 즉시 할인권 적용하려면 낮게 설정)</param>
        /// <returns></returns>
        public static Task<JObject> EnqueueAsync(ParkingDiscountModel discountModel, DiscountJobType jobType, int priority = 100)
        {
            var tcs = new TaskCompletionSource<JObject>(); // 작업 완료를 기다리는 Task 생성

            lock (_lock)
            {
                Logs.Info($"할인권 적용 요청을 받았습니다. 현재 할인권을 적용해야할 List는 총 {_ParkingDiscountPriorityQueue.Count}개 입니다");
                _ParkingDiscountPriorityQueue.Enqueue((discountModel, jobType, tcs), priority);
            }
            _semaphore.Release();// 큐 사용 완료 → 다음 대기 작업 실행 가능

            return tcs.Task;
        }

        private static async Task<JObject> RunCheckFeeAsync(string carNumber)
        {
            var page = await _context.NewPageAsync();
            await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/discount-search/original", new()
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            var result = await CheckParkingFeeOnlyAsync(carNumber, page);
            await page.CloseAsync();
            return result;
        }
        private static async Task<JObject> RunDiscountAsync(string carNumber, bool isGetOffWork, bool isUseDiscount = false, List<DiscountTicket> disCountList = null)
        {
            var page = _context.Pages.FirstOrDefault();
            if(page == null)
            {
                page = await _context.NewPageAsync();
            }
            await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/discount-search/original", new()
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            ParkingAccount mainParkingAccount = ParkingAccounts.FirstOrDefault();
            if (mainParkingAccount == null)
            {
                return new JObject
                {
                    ["Result"] = "Fail",
                    ["ReturnMessage"] = "No Parking Account Configured"
                };
            }
            page = await LoginDiscountPage(mainParkingAccount);
            var result = await RegisterParkingDiscountAsync(carNumber, page, isGetOffWork, isUseDiscount, disCountList);
            await page.CloseAsync();
            return result;
        }

        private static async Task<string> GetCurrentAccount(IPage page)
        {
            string userId = string.Empty;
            var userInfoElement = await page.QuerySelectorAsync("a[href='/nxpmsc/user-info'] span.nav-link-text");
            if(userInfoElement != null)
            {
                string userInfoText = await userInfoElement.InnerTextAsync();
                var match = Regex.Match(userInfoText, @"\(([^)]+)\)");
                if (match.Success)
                {
                    userId = match.Groups[1].Value.Trim();  // "C2115"
                    Console.WriteLine($"추출된 사용자 ID: {userId}");
                }
                else
                {
                    Console.WriteLine("사용자 ID를 찾을 수 없습니다.");
                }
            }
            return userId;
        }

        private static async Task<IPage?> LoginDiscountPage(ParkingAccount account)
        {

            var page = _context.Pages.FirstOrDefault();

            // 만약 없으면 새 페이지 생성 (예외 방지)
            if (page == null)
            {
                page = await _context.NewPageAsync();
                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });
            }
            else if (page.Url.Contains("original")) //original경로가 포함된 url로 들어갔으면 현재 로그인상태임
            {
                string currentUserId = await GetCurrentAccount(page);
                if(currentUserId.Trim().Equals(account.Id))
                {
                    return page;
                }
                page = await LogoutDiscountPage(page);
                page = await _context.NewPageAsync();
                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });
            }


            await page.FillAsync("#id", account.Id);
            await page.FillAsync("#password", account.Password);
            await page.ClickAsync("#loginBtn");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Logs.Info($"계정 {account.Id} 로그인 성공 ");
            return page;
        }

        private static async Task<IPage?> LogoutDiscountPage(IPage? page)
        {
            if (page == null)
            {
                Logs.Warn("LooutDiscountPage() 호출됨 - page가 null입니다.");
                return null;
            }

            if (page.IsClosed)
            {
                Logs.Warn("LooutDiscountPage() 호출됨 - page가 이미 닫혀있습니다.");
                return null;
            }

            try
            {
                Logs.Info("로그아웃 시도 중...");

                // 1. 네비게이션에서 로그아웃 아이콘 클릭 (모달 열기)
                var modalTrigger = page.Locator("a[data-target='#exampleModal']");
                if (await modalTrigger.IsVisibleAsync(new() { Timeout = 2000 }))
                {
                    await modalTrigger.ClickAsync();
                    Logs.Info("로그아웃 모달 열기 클릭 완료");

                    // 2. 모달이 열릴 때까지 대기
                    await page.WaitForSelectorAsync("#exampleModal.show", new() { Timeout = 3000 });

                    // 3. 모달 내부 로그아웃 버튼 클릭
                    var logoutButtonInModal = page.Locator("#exampleModal a.btn.btn-primary[href='/nxpmsc/logout']");
                    if (await logoutButtonInModal.IsVisibleAsync(new() { Timeout = 2000 }))
                    {
                        await logoutButtonInModal.ClickAsync();
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        Logs.Info("로그아웃 버튼 클릭 완료");
                    }
                    else
                    {
                        Logs.Warn("로그아웃 버튼을 모달 안에서 찾을 수 없습니다.");
                    }
                }
                else
                {
                    Logs.Warn("로그아웃 아이콘이 화면에 없습니다. (이미 로그아웃 상태일 수 있음)");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"LooutDiscountPage() 오류 발생: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (page != null && !page.IsClosed) //Playwright가 새로운계정으로 접속할때는 닫는게 좋다고해서(세션꼬일수있다고 해서) 시간좀 걸려도 닫게 수정
                    {
                        await page.CloseAsync();
                        Logs.Info("페이지 닫기 완료");
                    }
                }
                catch (Exception ex)
                {
                    Logs.Warn($"페이지 닫기 중 오류 발생: {ex.Message}");
                }
            }

            return null; // 닫았으니 항상 null 반환
        }
        private static async Task<JObject> CheckParkingFeeOnlyAsync(string carNumber, IPage page)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error"
            };

            try
            {
                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new() { WaitUntil = WaitUntilState.NetworkIdle });
                if (page.Url.Contains("login"))
                {
                    await page.FillAsync("#id", "C2115");
                    await page.FillAsync("#password", "6636");
                    await page.ClickAsync("#loginBtn");
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                }

                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/discount-search/original", new() { WaitUntil = WaitUntilState.NetworkIdle });

                await page.FillAsync("#carNo", $"{carNumber}");
                await page.ClickAsync("#btnCarSearch");
                await page.WaitForSelectorAsync("#searchDataTable tbody tr");

                var feeValueRaw = await page.Locator("#realFee").InputValueAsync();
                int feeValue = int.Parse(Regex.Replace(feeValueRaw, @"[^0-9]", ""));

                jobReturn["Result"] = "OK";
                jobReturn["ReturnMessage"] = $"차량번호 [{carNumber}]의 현재 주차요금은 {feeValue}원입니다.";
                jobReturn["Fee"] = feeValue;
            }
            catch (Exception ex)
            {
                jobReturn["ReturnMessage"] = ex.Message;
            }

            return jobReturn;
        }

        /// <summary>
        /// 주차권을 등록하는 비동기 함수
        /// </summary>
        /// <param name="carNumber">차량번호</param>
        /// <param name="page">브라우저 객체(Playwright)</param>
        /// <param name="isGetOffWork">현재 퇴근 여부(True = 현재시간 계산 False = 퇴근시간(6시) 기준)</param>
        /// <param name="isUseDiscount">API 요청한 할인권을 적용할지 여부</param>
        /// <param name="disCountList">API에서 호출한 할인권 목록(분단위)</param>
        /// <returns></returns>
        public static async Task<JObject> RegisterParkingDiscountAsync(string carNumber, IPage page
            , bool isGetOffWork = false, bool isUseDiscount = false, List<DiscountTicket> disCountList = null)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error",
                ["ResultType"] = Convert.ToInt32(DisCountResultType.Error)
            };
            try
            {
                //2025-09-17 로직변경
                /*
                 * GIDC건물 정책 및 계정 변화로 인해 여러 계정을 돌아가면서 할인권을 등록하게수정
                 * Setting.json   
                 *   "ParkingAccounts": [
                      {
                        "Id": "C2115",
                        "Password": "6636",
                        "IsMain": true
                      },
                      {
                        "Id": "C2102",
                        "Password": "6636",
                        "IsMain": false
                      },
                      {
                        "Id": "C2103",
                        "Password": "6636",
                        "IsMain": false
                      },
                      {
                        "Id": "C2116",
                        "Password": "6636",
                        "IsMain": false
                      }
                    ]
                    여기에 적여져있는 로그인 계정만큼 할인권을 등록 시도함
                    만약 로그인 실패하면 다음 계정으로 시도함
                */


                //가장처음은 메인계정으로 로그인되어있어 해당 정보가져오기
                //할인권 적용
                if (isUseDiscount == false)
                {
                    //각 계정별로 할인권 적용 시도
                    //요금과 별도로 일단 할인권 적용 시도
                    //메인계정은 마지막에 유료할인권 적용하게 가장 후순위로 정렬함.
              
                    foreach (var parkingAccount in ParkingAccounts)
                    {
                        try
                        {
                            Logs.Info($"차량번호{carNumber} {parkingAccount.Id}계정으로 기본 할인권 적용 시작");
                            page = await LoginDiscountPage(parkingAccount);

                            await SearchCar(carNumber, page).WaitAsync(new CancellationToken());
                            //차량번호와 입차시간 추출
                            List<string> carNoList = await ExtractCarList(page);
                            if (!IsEnteredCar(carNumber, carNoList,out jobReturn))
                            {
                                break;
                            }
                            await page.WaitForSelectorAsync($"a:has-text('{carNumber}')");
                            await page.ClickAsync($"a:has-text('{carNumber}')");

                            int feeValue = await GetRealParkingFee(page);
                          
                            jobReturn = await ApplyBasicDiscount(feeValue, carNumber, page, jobReturn, parkingAccount.Id);
                            feeValue =await GetRealParkingFee(page);
                            if (parkingAccount.IsMain && feeValue > 0)
                            {
                                Logs.Info($"차량번호{carNumber} 추가 유료 할인권 적용 시작");
                                jobReturn = await ApplyDiscount(feeValue, carNumber, page, jobReturn, isGetOffWork);
                            }
                            page = await LogoutDiscountPage(page);

                        }
                        catch (Exception ex)
                        {
                            Logs.Info($"계정 {parkingAccount.Id}로 할인권 적용중 오류 발생 {ex.Message}");
                        }
                    }
                }
                else
                {
                    var parkingAccount = ParkingAccounts.FirstOrDefault(a => a.IsMain);
                    page = await LoginDiscountPage(parkingAccount);

                    JArray returnJrray = new JArray();
                    await SearchCar(carNumber, page).WaitAsync(new CancellationToken());
                    //차량번호와 입차시간 추출
                    List<string> carNoList = await ExtractCarList(page);
                    if (!IsEnteredCar(carNumber, carNoList, out jobReturn))
                    {
                        return jobReturn;
                    }
                    await page.WaitForSelectorAsync($"a:has-text('{carNumber}')");
                    await page.ClickAsync($"a:has-text('{carNumber}')");
                    foreach (DiscountTicket applyDiscountTime in disCountList)
                    {
                        JObject disCountResult = new JObject();
                        disCountResult = await ApplyDiscount(carNumber, applyDiscountTime, page, disCountResult);
                        returnJrray.Add(disCountResult);
                    }
                    int okCount = returnJrray
                    .Where(obj => (string)obj["Result"] == "OK")
                    .Count();
                    if (disCountList.Count == okCount)
                    {
                        jobReturn["Result"] = "OK";
                        jobReturn["ReturnMessage"] = "전체 처리완료";
                        jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                    }
                    else
                    {
                        jobReturn["Result"] = "Fail";
                        jobReturn["ReturnMessage"] = "처리중 오류발생";
                        jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                    }
                    jobReturn.Add("results", returnJrray);
                }
            }
            catch (PlaywrightException ex)
            {
                if (ex.Message.Contains("Browser has been closed"))
                {
                    try { await _browser?.CloseAsync(); } catch { }
                    await RestartBrowserAsync();
                }
                jobReturn["ReturnMessage"] = "Playwright 예외 발생: " + ex.Message;
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                Logs.Info($"RegisterParkingDiscountAsync() Error : {ex.Message}");
            }
            catch (Exception ex)
            {
                jobReturn["ReturnMessage"] = ex.Message;
            }
            finally
            {
                // TODO: 메모리 누수 방지 및 할인권 적용 전략
                // 페이지를 닫지않으면 리다이렉트만해서 좀 더 페이지 생성 및 이동을 할 필요가없지만
                // 기본 권장사항은 페이지를 닫는것임.(Playwright 공식문서 참고) 
                // 다만 좀 더 할인권 적용 속도를 개선을 할 필요가잇으면 좀 더 전략적으로 페이지관리 방법을 생각해야함
                // 또한 페이지를 닫지않으면 Playwright가 메모리를 계속 사용하게 되므로(불필요한 JS 사용 및 GC동작이 안됨)
                // 현재는 메모리 누수 방지를위해 페이지를 닫음
                if(page != null && !page.IsClosed)
                {
                    await page.CloseAsync();
                }
                
            }
            return jobReturn;
        }

        private static async Task<List<string>> ExtractCarList(IPage page)
        {
            var row = await page.QuerySelectorAsync("#searchDataTable tbody tr");
            List<string> carNoList = new List<string>();
            if (row != null)
            {
                var carNoSpans = await page.Locator("table#searchDataTable span").AllInnerTextsAsync();

                foreach (var carNo in carNoSpans)
                {
                    Console.WriteLine($"차량번호: {carNo}");
                    carNoList.Add(carNo);
                }
            }
            return carNoList;
        }


        private static bool IsEnteredCar(string carNumber, List<string> carNoList, out JObject jobReturn)
        {
            bool bResult = false;
            jobReturn = new JObject();
            if (carNoList.Count == 0)
            {
                Logs.Info($"차량번호 {carNumber}는 입차 차량이 아닙니다 ");
                jobReturn["Result"] = "NoEnterCar";
                jobReturn["ReturnMessage"] = "차량번호 {carNumber}는 입차 차량이 아닙니다 ";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.NotFound);
            }
            else if (carNoList.Count > 1)
            {
                Logs.Info($"차량번호 {carNumber}로 조회한 차량번호가 2개 이상입니다.");
                jobReturn["Result"] = "Fail";
                jobReturn["ReturnMessage"] = $"차량번호 {carNumber}로 조회한 차량번호가 2개 이상입니다.";
                jobReturn["CarList"] = new JObject
                {
                    ["CarNumbers"] = new JArray(carNoList)
                };
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.CarMoreThanTwo);
            }
            else
            {
                bResult =true;
            }
            return bResult;
        }

        private static async Task<int> GetDiscountApplyAfterBalance(IPage page)
        {
            var feeElement = page.Locator("#realFee");
            // 2. value 추출
            string feeValueText = await feeElement.InputValueAsync(); // 예: "0 원"
                                                                      // 3. 숫자만 추출 (공백, 원 제거)
            string numericPart = System.Text.RegularExpressions.Regex.Replace(feeValueText, @"[^0-9]", "");
            int feeValue = int.Parse(numericPart);
            return feeValue;
        }
        private static async Task SearchCar(string carNum, IPage page)
        {
            page.SetDefaultTimeout(5000);
            string carNo = carNum;
            string encodedCarNo = System.Web.HttpUtility.UrlEncode(carNo);
      
            string searchUrl = $"http://gidc001.iptime.org:35052/nxpmsc/discount-search/original?btnSearchYn=Y&startDate={DateTime.Now.ToString("yyyy-MM-dd")}+00%3A00%3A00&endDate={DateTime.Now.ToString("yyyy-MM-dd")}+23%3A59%3A59&carNo={encodedCarNo}";
            await page.GotoAsync(searchUrl, new()
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            await page.FillAsync("#carNo", $"{carNum}", new() { Timeout = 3000 });
            await page.ClickAsync("#btnCarSearch");
            await page.WaitForSelectorAsync("#searchDataTable tbody tr");
        }

        /// <summary>
        /// 기본 할인권(계정별) 적용 퇴근여부와 별도로 적용
        /// </summary>
        /// <param name="feeValue">현재 주차요금</param>
        /// <param name="carNum">차량번호</param>
        /// <param name="page">브라우저 객체</param>
        /// <param name="jobReturn">결과를 전송받은 JObject</param>
        /// <returns></returns>
        private static async Task<JObject> ApplyBasicDiscount(int feeValue, string carNum, IPage page, JObject jobReturn, string loginAccount = "C2115")
        {
            var now = DateTime.Now;
            var today = now.DayOfWeek;
            // 휴일 여부 판단 (일요일 or 공휴일)
            bool isHoliday = today == DayOfWeek.Sunday || today == DayOfWeek.Saturday;
            string discountButtonText = isHoliday ? "방문자주차권(휴일)" : "방문자주차권";

            ILocator? discountButton = page.Locator("#add-discount-0"); //방문자주차권 (기본)

            if (isHoliday)
            {
                discountButton = page.Locator("#add-discount-1"); //방문자주차권 (휴일용)
            }

            string feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
            int feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", "")); //현재 주차요금 및 적용 이후 반영할 금액

            IReadOnlyList<IElementHandle> cancelButtons = await GetBasicParkingDisCountTicket(page); //적용한 방문자 할인권을 취소하는 버튼 취득
            //방문자 할인권을 찾았고 취소버튼이 2개 미만인 경우에만 할인권 적용 
            if (await discountButton.IsVisibleAsync() && cancelButtons.Count == 0) //방문자 할인권 적용 안함
            {

                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                // 금액 다시 확인
                feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
                feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
                Console.WriteLine($"기본 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
                feeValue = feeValueAfter; // 다음 할인권 적용을 위해 현재 금액 업데이트
                jobReturn["Result"] = "OK";
                jobReturn["LoginAccount"] = loginAccount;
                jobReturn["ReturnMessage"] = $"기본 할인권 적용완료 기본 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원";
                jobReturn["Balance"] = $"{feeValueAfter}";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.BasicTicket);

            }
            else if (!await discountButton.IsVisibleAsync())  //할인권 버튼을 못찾음
            {
                Console.WriteLine("방문자주차권 버튼을 찾을 수 없습니다.");
                jobReturn["Result"] = "Fail";
                jobReturn["LoginAccount"] = loginAccount;
                jobReturn["ReturnMessage"] = $"할인권 적용완료 기본 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원";
                jobReturn["Balance"] = $"{feeValueAfter}";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.BasicTicket);
            }
            return jobReturn;
        }



        /// <summary>
        /// 적용할 주차 할인권 버튼객체를 가져오는 함수
        /// </summary>
        /// <param name="page">브라우져 객체</param>
        /// <param name="discountMinutes">할인시간(할인권 버튼)</param>
        /// <returns></returns>
        private static ILocator? GetDiscountButton(IPage page, DiscountTicket discountMinutes)
        {
            //할인시간을 적용할 할인권갯수를 가져온다.
            ILocator? discountButton = null;
            switch (discountMinutes)
            {
                case DiscountTicket.Min30:
                    discountButton = page.Locator("#add-discount-2"); //30분권
                    break;
                case DiscountTicket.Hour1:
                    discountButton = page.Locator("#add-discount-3"); //1시간권
                    break;
                case DiscountTicket.Hour4:
                    discountButton = page.Locator("#add-discount-4"); //4시간권
                    break;
            }
            return discountButton;
        }
        /// <summary>
        /// 할인권적용 (요청받은 유료 할인권만 적용)
        /// </summary>
        /// <param name="carNum"></param>
        /// <param name="discountMinutes">적용할 시간(시간별 할인권이 존재)</param>
        /// <param name="page"></param>
        /// <param name="jobReturn"></param>
        /// <returns></returns>
        private static async Task<JObject> ApplyDiscount(string carNum, DiscountTicket discountMinutes, IPage page, JObject jobReturn)
        {
            ILocator? discountButton = GetDiscountButton(page, discountMinutes);
            if (discountButton == null)
            {
                jobReturn["Result"] = "Fail";
                jobReturn["DiscountTicket"] = EnumExtensions.GetDescription(discountMinutes);
                jobReturn["ReturnMessage"] = $"{EnumExtensions.GetDescription(discountMinutes)}분권 할인권 버튼을 찾을 수 없습니다.";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.PaidTicket);
                return jobReturn;
            }
            try
            {
                Dictionary<string, int> ticketCount = await GetTicketCountDictionary(page); //할인권 갯수 가져오기
                int feeValueAfter = await GetRealParkingFee(page);
                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValueAfter);
                // 금액 다시 확인
                feeValueAfter = await GetRealParkingFee(page);
                jobReturn["Result"] = "OK";
                jobReturn["DiscountTicket"] = EnumExtensions.GetDescription(discountMinutes);
                jobReturn["ReturnMessage"] = $"{EnumExtensions.GetDescription(discountMinutes)}할인권 적용완료 남은주차요금 {feeValueAfter}원";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.PaidTicket);
            }
            catch (Exception ex)
            {
                jobReturn["Result"] = "Fail";
                jobReturn["DiscountTicket"] = EnumExtensions.GetDescription(discountMinutes);
                jobReturn["ReturnMessage"] = $"{EnumExtensions.GetDescription(discountMinutes)}할인권 적용 중 오류 발생 {Environment.NewLine}{ex.Message}";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.PaidTicket);
                return jobReturn;
            }
            return jobReturn;
        }


        /// <summary>
        /// 할인권 적용(기본적용이후 남는 금액은 유료할인권)
        /// </summary>
        /// <param name="feeValue">현재 주차요금</param>
        /// <param name="carNum">차량번호</param>
        /// <param name="page">브라우저 객체</param>
        /// <param name="jobReturn">결과를 전송받은 JObject</param>
        /// <param name="isGetOffWork">현재 퇴근 여부(현재시간 계산 or 퇴근시간 기준)</param>
        /// <returns>jobReturn</returns>
        private static async Task<JObject> ApplyDiscount(int feeValue, string carNum, IPage page, JObject jobReturn, bool isGetOffWork = false)
        {
            int feeValueAfter = await GetRealParkingFee(page);
            if (feeValueAfter > 0)
            {
                Dictionary<string, int> ticketCount = await GetTicketCountDictionary(page); //할인권 갯수 가져오기
                IElementHandle? input = await page.QuerySelectorAsync("#parkingMin");
                IElementHandle? discountInput = await page.QuerySelectorAsync("#totDc");

                int totalParkingMinute = -1;

                int totalDiscountedFee = 0; //이미 할인받은 시간
                                            //총 할인요금
                if (discountInput != null)
                {
                    string discountValue = await discountInput.GetAttributeAsync("value") ?? "0";
                    if (!int.TryParse(Regex.Replace(discountValue, @"[^0-9]", ""), out totalDiscountedFee))
                    {
                        totalDiscountedFee = 0;
                    }
                }
                //총 주차시간
                if (input != null)
                {
                    string parkingTimeText = await input!.GetAttributeAsync("value") ?? "";
                    totalParkingMinute = GetTotalParkingMinutes(parkingTimeText, isGetOffWork);
                }

                if (totalParkingMinute != -1)
                {
                    try
                    {
                        ILocator? discountButton = null;//할인권 버튼

                        DiscountInventory discountInventory = new DiscountInventory();
                        discountInventory.Count30Min = ticketCount.ContainsKey("discount30Min") ? ticketCount["discount30Min"] : 0;
                        discountInventory.Count1Hour = ticketCount.ContainsKey("discount1Hour") ? ticketCount["discount1Hour"] : 0;
                        discountInventory.Count4Hour = ticketCount.ContainsKey("discount4Hour") ? ticketCount["discount4Hour"] : 0;
                        //전체 할인받은 금액을 시간으로 환산
                        double discountedMinutesRaw = (totalDiscountedFee / ParkingDiscountManager.FEE_PER_TIME_BLOCK) * ParkingDiscountManager.TIME_BLOCK_MINUTES;
                        int totalDiscountedMinutes = (int)Math.Ceiling(discountedMinutesRaw);
                        ParkingDiscountPlan discountPlan = ApplyDiscountTicketsWithInventory(feeValueAfter, totalParkingMinute, totalDiscountedMinutes, discountInventory, BUFFER_OUT_TIME_MINUTES);
                        if (discountPlan.Use30Min > 0)
                        {
                            discountButton = page.Locator("#add-discount-2"); //30분 할인권버튼
                            for (int i = 0; i < discountPlan.Use30Min; i++)
                            {
                                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                                // 금액 다시 확인
                                feeValueAfter = await GetRealParkingFee(page);
                                Console.WriteLine($"Use30Min : {i + 1}번째 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
                            }
                        }
                        if (discountPlan.Use1Hour > 0)
                        {
                            discountButton = page.Locator("#add-discount-3"); //1시간 할인권버튼
                            for (int i = 0; i < discountPlan.Use1Hour; i++)
                            {
                                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                                // 금액 다시 확인
                                feeValueAfter = await GetRealParkingFee(page);
                                Console.WriteLine($"Use30Min : {i + 1}번째 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
                            }
                        }
                        if (discountPlan.Use4Hour > 0)
                        {
                            discountButton = page.Locator("#add-discount-4"); //4시간 할인권버튼
                            for (int i = 0; i < discountPlan.Use4Hour; i++)
                            {
                                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                                // 금액 다시 확인
                                feeValueAfter = await GetRealParkingFee(page);
                                Console.WriteLine($"Use30Min : {i + 1}번째 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
                            }
                        }
                        
                        if (jobReturn.ContainsKey("Balance"))
                        {
                            jobReturn["Balance"] = feeValueAfter;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Info($"유료 할인권 적용 중 오류 발생 {ex.Message}");
                        Logs.Info($"유료 할인권 적용 중 오류 발생 {ex.ToString()}");
                    }
                }

                if (feeValueAfter == 0)
                {
                    jobReturn["Result"] = "OK";
                    jobReturn["ReturnMessage"] = $"차량번호: {carNum} 할인권 적용완료.";
                    jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                    jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.PaidTicket);
                    Logs.Info($"차량번호: {carNum} 할인권 적용완료.");
                }
                else
                {
                    jobReturn["Result"] = "OK";
                    jobReturn["ReturnMessage"] = $"차량번호: {carNum} 할인권 적용을 했지만 잔액 존재";
                    jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.SuccessButFee);
                    jobReturn["DiscountType"] = EnumExtensions.GetDescription(DiscountType.PaidTicket);
                    Logs.Info($"차량번호: {carNum} 할인권 적용했지만 잔액이 존재함.");
                }
            }
            else
            {
                jobReturn["Result"] = "NoChargeFee";
                jobReturn["ReturnMessage"] = $"지불할 금액이없습니다.";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);

            }
            return jobReturn;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputMsg">주차시간(0일 7시 5분) </param>
        /// <param name="isGetOffWork">퇴근여부 True = 퇴근 False = 배치 동작시간</param>
        /// <returns></returns>
        private static int GetTotalParkingMinutes(string inputMsg, bool isGetOffWork = false)
        {
            int totalMinutes = 0;

            // 일
            var dayMatch = Regex.Match(inputMsg, @"(\d+)\s*일");
            if (dayMatch.Success)
            {
                totalMinutes += int.Parse(dayMatch.Groups[1].Value) * 1440;
            }

            // 시
            var hourMatch = Regex.Match(inputMsg, @"(\d+)\s*시");
            if (hourMatch.Success)
            {
                totalMinutes += int.Parse(hourMatch.Groups[1].Value) * 60;
            }

            // 분
            var minMatch = Regex.Match(inputMsg, @"(\d+)\s*분");
            if (minMatch.Success)
            {
                totalMinutes += int.Parse(minMatch.Groups[1].Value);
            }
            Console.WriteLine($"현재까지 입차시간 {totalMinutes}분");
            //배치로 돌리는 차량은 GET_OFF_WORK_TIME이후에 출차기준으로 추가 시간 부여
            if (!isGetOffWork && TimeOnly.FromDateTime(DateTime.Now) <= GET_OFF_WORK_TIME)
            {
                TimeOnly now = TimeOnly.FromDateTime(DateTime.Now);
                totalMinutes += (int)(GET_OFF_WORK_TIME.ToTimeSpan() - now.ToTimeSpan()).TotalMinutes;
                Console.WriteLine($"현재시간 {DateTime.Now.ToString("HH:mm:ss")}입니다 출차시간을 {GET_OFF_WORK_TIME.ToString("HH:mm:ss")}기준으로 하여 출차시간을 {totalMinutes}분으로 계산하여 진행합니다");
                Logs.Info($"현재시간 {DateTime.Now.ToString("HH:mm:ss")}입니다 출차시간을 {GET_OFF_WORK_TIME.ToString("HH:mm:ss")}기준으로 하여 출차시간을 {totalMinutes}분으로 계산하여 진행합니다");
            }

            totalMinutes -= FREE_PARKING_MINUTES; //기본 무료 주차시간

            return totalMinutes;
        }

        private static async Task<int> GetRealParkingFee(IPage page)
        {
            string feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
            int feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
            return feeValueAfter; //현재 주차요금
        }

        private static async Task<string> GetMessageFromClickParkingDisCountTicketButton(IPage page, ILocator? discountButton, int feeValue)
        {
            //버튼클릭했을때 나오는 dialog 문구를 받아오는 string 변수
            string alertMessage = "";
            //할인권 적용했을때 결과메세지 수신이벤트
            TaskCompletionSource<IDialog> dialogTcs = new TaskCompletionSource<IDialog>();
            page.Dialog += (_, dialog) =>
            {
                alertMessage = dialog.Message;
                dialog.AcceptAsync();
                dialogTcs.TrySetResult(dialog);
            };

            await discountButton!.ClickAsync();

            //Dialog가 나타날 때까지 기다림 (최대 5초)
            var dialogTask = dialogTcs.Task;
            if (await Task.WhenAny(dialogTask, Task.Delay(5000)) == dialogTask)
            {
                var dialog = await dialogTask;
            }

            await page.WaitForFunctionAsync(
             "(prev) => document.querySelector('#realFee')?.value !== prev",
               feeValue, // 최대 5초 기다림
               new() { Timeout = 5000 }
           );
            return alertMessage; //dialog에서 받은 메세지 반환
        }


        private static async Task<IReadOnlyList<IElementHandle>> GetBasicParkingDisCountTicket(IPage page)
        {
            var result = new List<IElementHandle>();
            var rows = await page.QuerySelectorAllAsync("table tbody tr");

            foreach (var row in rows)
            {
                // 각 행의 td 목록 가져오기
                var cells = await row.QuerySelectorAllAsync("td");
                if (cells.Count < 2) continue;

                var titleText = (await cells[0].InnerTextAsync()).Trim();

                if (titleText == "방문자주차권")
                {
                    // 5열의 버튼 찾기
                    var cancelButton = await cells[4].QuerySelectorAsync("button[id^='delete']");

                    if (cancelButton != null)
                    {
                        result.Add(cancelButton);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 실제 남은 할인권과 주차요금에따라 할인권을 적용하는 함수
        /// </summary>
        /// <param name="realFee">실제 계산할 주차요금</param>
        /// <param name="totalRealParkingMinutes">전체 주차시간(최초 30분을 제외한 실제 부과할 요금시간)</param>
        /// <param name="alreadyDiscountedMinutes">이미 사전 할인받은 금액</param>
        /// <param name="inventory">주차권 종류 및 수량</param>
        /// <param name="bufferMinutes">출차 보장 시간</param>
        /// <returns></returns>
        public static ParkingDiscountPlan ApplyDiscountTicketsWithInventory(int realFee, int totalRealParkingMinutes, int alreadyDiscountedMinutes, DiscountInventory inventory, int bufferMinutes = 15)
        {
            // 1. 요금 기준 최대 커버 시간
            int feeBlocks = (int)Math.Ceiling(realFee / (double)FEE_PER_TIME_BLOCK);
            int maxMinutesByFee = feeBlocks * TIME_BLOCK_MINUTES;

            // 2. 실제 필요한 추가 할인 시간 (실주차 - 기존 할인 + 여유시간)
            int requiredMinutes = Math.Max(0, (totalRealParkingMinutes - alreadyDiscountedMinutes) + bufferMinutes);

            // 3. 실제 할인 적용할 시간 = 요금 기준 한도 vs 실제 필요한 추가 시간 중 큰값
            // 버퍼시간까지 1. 고려한 할인권 적용시 최대 보장시간과 2. 현재 주차  시간을 구한다음 더 큰값을 선택 
            int targetMinutes = Math.Max(requiredMinutes, maxMinutesByFee);

            // 다음 요금이 부과되는 분을 구하기위해 잔여분 기억
            // 이 값이 양수라면 → 요금 커버 기준 때문에 할인권이 과도하게 사용된 상태
            int overCoveredMinutes = maxMinutesByFee - requiredMinutes;

            var result = new ParkingDiscountPlan();

            // 4. 그리디 적용(가장 큰 단위부터 적용)
            int use4h = Math.Min(targetMinutes / 240, inventory.Count4Hour);
            result.Use4Hour = use4h;
            targetMinutes -= use4h * 240;

            int use1h = Math.Min(targetMinutes / 60, inventory.Count1Hour);
            result.Use1Hour = use1h;
            targetMinutes -= use1h * 60;
            // 1시간 단위까지 쓰고 남은 시간 확인
            int remainingAfter1h = targetMinutes % 60;

            // 만약 31~59분이 남았고, 1시간권 재고가 있으면
            if (remainingAfter1h > 30 && inventory.Count1Hour > result.Use1Hour)
            {
                // 30분권 대신 1시간권을 1장 더 사용
                result.Use1Hour += 1;
                targetMinutes -= 60;
            }
            else
            {
                // 그 외에는 기존처럼 30분권 적용
                int use30m = Math.Min(targetMinutes / 30, inventory.Count30Min);
                result.Use30Min = use30m;
                targetMinutes -= use30m * 30;
            }
            //3.에서 선택하고 남은 잔여시간
            result.UncoveredMinutes = targetMinutes;
            Console.WriteLine($"result.UncoveredMinutes : {result.UncoveredMinutes}");

            // 추가: 다음 요금 부과까지 남은 시간 계산
            //if (result.UncoveredMinutes < 0)
            //{
            //    // 아직 요금이 발생되지 않았고, 출차해도 무방한 시간
            //    result.MinutesUntilNextFee = -result.UncoveredMinutes;
            //    Console.WriteLine($"추가 요금 없이 출차 가능한 남은 시간: {result.MinutesUntilNextFee}분");
            //}
            //else if (result.UncoveredMinutes == 0)
            //{
            //    // 바로 요금 발생 가능성 있음
            //    result.MinutesUntilNextFee = 0;
            //    Console.WriteLine($"요금 발생 임박!");
            //}
            //else
            //{
            //    // FEE_BLOCK_MINUTES 기준으로 다음 요금 부과 시점 계산
            //    int mod = result.UncoveredMinutes % FEE_BLOCK_MINUTES;
            //    result.MinutesUntilNextFee = mod == 0 ? 0 : FEE_BLOCK_MINUTES - mod;
            //    Console.WriteLine($"다음 요금 발생까지 남은 시간: {result.MinutesUntilNextFee}분");
            //}
            return result;
        }


        private static async Task<JObject> GetParkingFee(IPage page, int feeValue, JObject jobReturn, string carNum)
        {
            await page.WaitForFunctionAsync(
               "(prev) => document.querySelector('#realFee')?.value !== prev",
                 feeValue, // 최대 5초 기다림
                 new() { Timeout = 5000 }
             );

            // 금액 다시 확인
            string feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
            int feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
            Console.WriteLine($"할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");

            jobReturn["Result"] = "OK";
            jobReturn["ReturnMessage"] = $"차량번호:[{carNum}] 방문자 주차권이 적용되었습니다. 할인권 적용 후 주차금액: {feeValue}원 => {feeValueAfter}원";
            return jobReturn;
        }

        private static async Task<Dictionary<string, int>> GetTicketCountDictionary(IPage page)
        {
            var ticketSelectors = new Dictionary<string, string>
            {
                { "discount30Min", "#add-discount-2" }, // 방문자주차권 (30분)
                { "discount1Hour", "#add-discount-3" }, // 방문자주차권 (1시간)
                { "discount4Hour", "#add-discount-4" }, // 방문자주차권 (4시간)
                { "discountAllDay", "#add-discount-5" } // 방문자주차권 (일일권)
            };

            var ticketCount = new Dictionary<string, int>();

            foreach (var kvp in ticketSelectors)
            {
                var locator = page.Locator(kvp.Value);
                string buttonText = await locator.InnerTextAsync();

                var match = Regex.Match(buttonText, @"\((\d+)\)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                {
                    ticketCount[kvp.Key] = count;
                    Console.WriteLine($"{kvp.Key} 할인 티켓 남은 장수: {count}");
                }
                else
                {
                    Console.WriteLine($"{kvp.Key}에서 숫자를 찾을 수 없습니다.");
                }
            }

            return ticketCount;
        }
        private static async Task RestartBrowserAsync()
        {
            try { await _context?.CloseAsync(); } catch { }
            try { await _browser?.CloseAsync(); } catch { }

            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage" }
            });

            if (File.Exists(ParkingDiscountManager.SessionFilePath))
            {
                _context = await _browser.NewContextAsync(new()
                {
                    StorageStatePath = ParkingDiscountManager.SessionFilePath,
                    IgnoreHTTPSErrors = true,
                    ViewportSize = null,
                    BypassCSP = true
                });
            }
            else
            {
                var tempContext = await _browser.NewContextAsync();
                var page = await tempContext.NewPageAsync();

                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new() { WaitUntil = WaitUntilState.NetworkIdle });
                await page.FillAsync("#id", "C2115");
                await page.FillAsync("#password", "6636");
                await page.ClickAsync("#loginBtn");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                string storageStateJson = await tempContext.StorageStateAsync();
                await File.WriteAllTextAsync(ParkingDiscountManager.SessionFilePath, storageStateJson);
                await tempContext.CloseAsync();

                _context = await _browser.NewContextAsync(new()
                {
                    StorageStatePath = ParkingDiscountManager.SessionFilePath,
                    IgnoreHTTPSErrors = true,
                    ViewportSize = null,
                    BypassCSP = true
                });
            }
        }

    }

}