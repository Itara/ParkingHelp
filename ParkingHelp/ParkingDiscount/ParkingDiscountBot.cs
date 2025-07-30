using log4net;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.Logging;
using ParkingHelp.Models;
using ParkingHelp.ParkingDiscount;
using ParkingHelp.SlackBot;
using System.Diagnostics;
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

        /// <summary>
        /// 시간 블록당 요금 (예: TIME_BLOCK_MINUTES당 2000원)
        /// </summary>
        public const int FEE_PER_TIME_BLOCK = 2000;

        /// <summary>
        /// 요금이 적용되는 시간 블록 단위 
        /// </summary>
        public const int TIME_BLOCK_MINUTES = 30;


        public static event EventHandler<ParkingDiscountResultEventArgs>? OnParkingDiscountEvent; //주차 결과 이벤트

        public static void Initialize(IServiceProvider services, IConfiguration config)
        {
            //DI주입
            _services = services;
            _config = config;
            //Slack 설정
            slackOptions = _services.GetRequiredService<SlackOptions>();
            slackNotifier = new SlackNotifier(slackOptions);
            //주차등록 결과 이벤트
            OnParkingDiscountEvent += PlaywrightManager_OnParkingDiscountEvent;

            //주차할인권 등록리스트를 받을 비동기 작업 시작

            _ = Task.Run(async () =>
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new()
                {
                    Headless = true,
                    Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage" }
                });
                _context = await _browser.NewContextAsync();

                bool isOnlyFirstRun = true; //즉시 실행이면 한번만 실행한다 
                string? autoDiscountTime = _config["AutoDiscountTime"];
                TimeOnly? lastRunTime = null;

                Logs.Info($"자동 할인권 적용 시간 {autoDiscountTime ?? ""}");
                while (true)
                {
                    //배치시작시간
                    TimeOnly currentTime = TimeOnly.FromDateTime(DateTime.Now);
                    if (TimeOnly.TryParse(_config["AutoDiscountTime"], out _AutoDisCountApplyTime)
                        && _AutoDisCountApplyTime.Hour == currentTime.Hour
                        && _AutoDisCountApplyTime.Minute == currentTime.Minute
                        && (!lastRunTime.HasValue || lastRunTime.Value.Hour != currentTime.Hour || lastRunTime.Value.Minute != currentTime.Minute)) //같은 시간에 중복실행 방지
                    {
                        lastRunTime = new TimeOnly(currentTime.Hour, currentTime.Minute);
                        Console.WriteLine("할인권 적용 시간입니다. 할인권 등록을위해 사용자 조회 시작합니다.");
                        Logs.Info("할인권 적용 시간입니다. 할인권 등록을위해 사용자 조회 시작합니다.");
                        List<MemberDto> members = GetMemberList();
                        foreach (MemberDto meber in members)
                        {
                            foreach (var car in meber.Cars)
                            {
                                //자동 할인권 적용 작업큐에 추가
                                string memberEmail = meber.Email ?? string.Empty;
                                int priority = 100; //기본 우선순위는 100, 필요시 조정 가능
                                ParkingDiscountModel discountModel = new ParkingDiscountModel(car.CarNumber, memberEmail, true);
                                _ = EnqueueAsync(discountModel, DiscountJobType.ApplyDiscount, priority);
                            }
                        }
                    }
                    else if (_config["AutoDiscountTime"] != null && _config["AutoDiscountTime"].Equals("NOW", StringComparison.CurrentCultureIgnoreCase) && isOnlyFirstRun)
                    {
                        Console.WriteLine("할인권 즉시 적용 상태입니다. 할인권 등록을위해 사용자 조회 시작합니다. 이작업은 작업은 한번만 실행됩니다.");
                        Logs.Info("할인권 즉시 적용 상태입니다. 할인권 등록을위해 사용자 조회 시작합니다..");
                        List<MemberDto> members = GetMemberList();
                        foreach (MemberDto meber in members)
                        {
                            foreach (var car in meber.Cars)
                            {
                                //자동 할인권 적용 작업큐에 추가
                                string memberEmail = meber.Email ?? string.Empty;
                                int priority = 100;
                                ParkingDiscountModel discountModel = new ParkingDiscountModel(car.CarNumber, memberEmail, true);
                                _ = EnqueueAsync(discountModel, DiscountJobType.ApplyDiscount, priority);
                            }
                        }
                        isOnlyFirstRun = false; //즉시 실행은 한번만 실행
                        Logs.Info("주차할인권 즉시 적용");
                    }
                    if (_ParkingDiscountPriorityQueue.Count == 0)
                    {
                        Console.WriteLine("Queue Count 0....");
                        await Task.Delay(500);
                        continue;
                    }

                    //큐 동기화 설정
                    await _semaphore.WaitAsync();

                    (ParkingDiscountModel ParkingDisCountModel, DiscountJobType jobType, TaskCompletionSource<JObject> tcs) item;

                    //Lock을 사용하여 작업 큐에서 항목을 안전하게 가져옴
                    lock (_lock)
                    {
                        //PriorityQueue로 생성해서 우선순위가 높은것부터 뽑아옴
                        //우선순위
                        // 1. API로 호출된 차량 (High)
                        // 2. 퇴근 등록을 한 차량 (Medium)
                        // 3. 배치 시간이 되서 작업시간이 된 차량 (Low)
                        _ParkingDiscountPriorityQueue.TryDequeue(out item, out int priority);
                    }

                    try
                    {
                        ParkingDiscountModel parkingDiscountModel = item.ParkingDisCountModel;
                        var result = item.jobType switch
                        {
                            DiscountJobType.ApplyDiscount => await RunDiscountAsync(parkingDiscountModel.CarNumber),
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
                        Console.WriteLine($"작업중 오류 발생 {ex.Message}");
                        Console.WriteLine($"작업중 오류 발생 {ex.StackTrace}");
                    }
                }
            });
        }

        private static async Task CheckAutoApplyTimeForMemberDiscount(bool isOnlyFirstRun = false)
        {

        }

        private static void PlaywrightManager_OnParkingDiscountEvent(object? sender, ParkingDiscountResultEventArgs e)
        {
            Console.WriteLine($"차량번호: {e.CarNumber} 할인권 적용 결과: {e.Result["Result"]} Message : {e.Result["ReturnMessage"]} ");
            _ = SendParkingDiscountResult(e); //비동기로 슬랙에 결과 알림
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
                string userId = userInfo?.Id ?? "";
                userId = !string.IsNullOrEmpty(userId) ? $"<@{userId}>" : string.Empty;
                switch (Convert.ToInt32(e.Result["ResultType"]))
                {
                    case (int)DisCountResultType.Success:
                        Logs.Info($"차량번호: {carNumber} 할인권 적용 완료");
                        break;
                    case (int)DisCountResultType.SuccessButFee:
                        await slackNotifier.SendMessageAsync($"{userId} {e.Result["ReturnMessage"]}", null);
                        break;
                    case (int)DisCountResultType.CarMoreThanTwo:
                        await slackNotifier.SendMessageAsync($"{userId} 차량번호: {carNumber} 할인권 적용 결과: 차량정보가 2대 이상입니다.", null);
                        break;
                    case (int)DisCountResultType.AlreadyUse:
                        await slackNotifier.SendMessageAsync($"{userId} 차량번호: {carNumber} 이미 방문자 할인권을 사용했습니다.", null);
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
            return members;
        }

        /// <summary>
        /// 할인권 적용 작업큐에 신규 리스트 추가
        /// </summary>
        /// <param name="carNumber"></param>
        /// <param name="jobType">주차요금 조회 or 할인권 조회</param>
        /// <param name="priority">실행 우선순위 (0이 될수록 우선순위 증가 즉시 할인권 적용하려면 낮게 설정)</param>
        /// <returns></returns>
        public static Task<JObject> EnqueueAsync(ParkingDiscountModel discountModel, DiscountJobType jobType, int priority = 100)
        {
            var tcs = new TaskCompletionSource<JObject>(); // 작업 완료를 기다리는 Task 생성

            lock (_lock)
            {
                Console.WriteLine($"할인권 적용 요청을 받았습니다. 현재 할인권을 적용해야할 List는 총 {_ParkingDiscountPriorityQueue.Count}개 입니다");
                _ParkingDiscountPriorityQueue.Enqueue((discountModel, jobType, tcs), priority);
            }
            _semaphore.Release();// 큐 사용 완료 → 다음 대기 작업 실행 가능

            return tcs.Task;
        }

        private static async Task<JObject> RunCheckFeeAsync(string carNumber)
        {
            var page = await _context.NewPageAsync();
            var result = await CheckParkingFeeOnlyAsync(carNumber, page);
            await page.CloseAsync();
            return result;
        }
        private static async Task<JObject> RunDiscountAsync(string carNumber)
        {
            var page = await _context.NewPageAsync(); // context 재사용
            var result = await RegisterParkingDiscountAsync(carNumber, page);
            await page.CloseAsync();
            return result;
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
        /// <param name="notifySlackChannel">해당 결과를 슬랙 채널에 결과 전송을 할지</param>
        /// <returns></returns>
        public static async Task<JObject> RegisterParkingDiscountAsync(string carNumber, IPage page, bool notifySlackChannel = false)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error",
                ["ResultType"] = Convert.ToInt32(DisCountResultType.Error)
            };
            try
            {
                Console.WriteLine("로그인 페이지 이동 중...");
                if (page.Url.Contains("login"))
                {
                    Console.WriteLine("로그인 필요 → 로그인 시작");
                }
                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });

                if (page.Url.Contains("login")) //로그인 이후 자동으로 메인페이지 리다이렉트됨
                {
                    await page.WaitForSelectorAsync("#id");
                    await page.FillAsync("#id", "C2115");
                    await page.FillAsync("#password", "6636");
                    await page.ClickAsync("#loginBtn");
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    Console.WriteLine("로그인 완료");
                }
                else
                {
                    await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/discount-search/original", new()
                    {
                        WaitUntil = WaitUntilState.NetworkIdle
                    });
                }

                // 로그인 후 URL 또는 특정 요소 대기 (필요시 수정)
                // 1. 차량번호 텍스트박스 입력
                await page.FillAsync("#carNo", $"{carNumber}");
                await page.ClickAsync("#btnCarSearch");
                await page.WaitForSelectorAsync("#searchDataTable tbody tr");

                // 4. 첫 번째 행에서 차량번호와 입차시간 추출
                var row = await page.QuerySelectorAsync("#searchDataTable tbody tr");
                if (row != null)
                {
                    var carNoSpans = await page.Locator("table#searchDataTable span").AllInnerTextsAsync();
                    List<string> carNoList = new List<string>();
                    foreach (var carNo in carNoSpans)
                    {
                        Console.WriteLine($"차량번호: {carNo}");
                        carNoList.Add(carNo);
                    }

                    if (carNoList.Count == 1)
                    {
                        string carNum = carNoList[0];
                        await page.WaitForSelectorAsync($"a:has-text('{carNum}')");
                        await page.ClickAsync($"a:has-text('{carNum}')");

                        var feeElement = page.Locator("#realFee");

                        // 2. value 추출
                        string feeValueText = await feeElement.InputValueAsync(); // 예: "0 원"

                        // 3. 숫자만 추출 (공백, 원 제거)
                        string numericPart = System.Text.RegularExpressions.Regex.Replace(feeValueText, @"[^0-9]", "");
                        int feeValue = int.Parse(numericPart);

                       
                        jobReturn = await ApplyDiscount(feeValue, carNum, page, jobReturn);
                     
                    }
                    else if (carNoList.Count > 1)
                    {
                        jobReturn = new JObject
                        {
                            ["Result"] = "Fail",
                            ["ReturnMessage"] = $"{carNumber}로 조회한 차량번호가 2개 이상입니다.",
                            ["CarList"] = new JObject
                            {
                                ["CarNumbers"] = new JArray(carNoList)
                            },
                            ["ResultType"] = Convert.ToInt32(DisCountResultType.CarMoreThanTwo)
                        };
                    }
                    else if (carNoList.Count < 1)
                    {
                        jobReturn = new JObject
                        {
                            ["Result"] = "Fail",
                            ["ReturnMessage"] = $"차량번호 {carNumber}는 미등록 차량입니다.",
                            ["ResultType"] = Convert.ToInt32(DisCountResultType.NotFound)
                        };
                    }
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
            }
            catch (Exception ex)
            {
                jobReturn["ReturnMessage"] = ex.Message;
            }
            finally
            {
                await page.CloseAsync(); // 메모리 누수 방지
            }
            return jobReturn;
        }

        /// <summary>
        /// 할인권 적용
        /// </summary>
        /// <param name="feeValue">현재 주차요금</param>
        /// <param name="carNum">차량번호</param>
        /// <param name="page">브라우저 객체</param>
        /// <param name="jobReturn">결과를 전송받은 JObject</param>
        /// <returns>jobReturn</returns>
        private static async Task<JObject> ApplyDiscount(int feeValue, string carNum, IPage page, JObject jobReturn)
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

            IReadOnlyList<IElementHandle> cancelButtons = await GetBasicParkingDisCountTicket(page); //적용한 방문자 할인권을 취소하는 버튼
            //방문자 할인권을 찾았고 취소버튼이 2개 미만인 경우에만 할인권 적용 
            if (await discountButton.IsVisibleAsync() && cancelButtons.Count == 0) //방문자 할인권 적용 안함
            {
                for(int i = 0; i < 2; i++)
                {
                    string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                    // 금액 다시 확인
                    feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
                    feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
                    Console.WriteLine($"{i+1}번째 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
                    feeValue = feeValueAfter; // 다음 할인권 적용을 위해 현재 금액 업데이트
                }
            }
            else if (await discountButton.IsVisibleAsync() && cancelButtons.Count == 1) //방문자 할인권 1장적용
            {
                string message = await GetMessageFromClickParkingDisCountTicketButton(page, discountButton, feeValue);
                // 금액 다시 확인
                feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
                feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
                Console.WriteLine($"첫번째 할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");
            }
            else if (!await discountButton.IsVisibleAsync())  //할인권 버튼을 못찾음
            {
                Console.WriteLine("방문자주차권 버튼을 찾을 수 없습니다.");
                jobReturn["Result"] = "Fail";
                jobReturn["ReturnMessage"] = "방문자주차권 버튼을 찾을 수 없습니다.";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);
                return jobReturn;
            }

            //기본 할인권 요금 확인후 금액이 남아있으면 유료할인권 적용
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
                    if(!int.TryParse(Regex.Replace(discountValue, @"[^0-9]", ""),out totalDiscountedFee))
                    {
                        totalDiscountedFee = 0;
                    }
                }
                //총 주차시간
                if (input != null)
                {
                    string parkingTimeText = await input!.GetAttributeAsync("value") ?? "";
                    totalParkingMinute = GetTotalParkingMinutes(parkingTimeText);
                }

                if(totalParkingMinute != -1)
                {
                    DiscountInventory discountInventory = new DiscountInventory();
                    discountInventory.Count30Min = ticketCount.ContainsKey("discount30Min") ? ticketCount["discount30Min"] : 0;
                    discountInventory.Count1Hour = ticketCount.ContainsKey("discount1Hour") ? ticketCount["discount1Hour"] : 0;
                    discountInventory.Count4Hour = ticketCount.ContainsKey("discount4Hour") ? ticketCount["discount4Hour"] : 0;
                    //전체 할인받은 금액을 시간으로 환산
                    double discountedMinutesRaw = (totalDiscountedFee / ParkingDiscountManager.FEE_PER_TIME_BLOCK) * ParkingDiscountManager.TIME_BLOCK_MINUTES; 
                    int totalDiscountedMinutes = (int)Math.Ceiling(discountedMinutesRaw);
                    ParkingDiscountPlan discountPlan = ApplyDiscountTicketsWithInventory(feeValueAfter, totalParkingMinute, totalDiscountedMinutes, discountInventory, 15);
                    if(discountPlan.Use30Min > 0)
                    {
                        discountButton = page.Locator("#add-discount-2"); //30분 할인권버튼
                        for(int i=0;i< discountPlan.Use30Min; i++)
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
                }
                if(feeValueAfter == 0)
                {
                    jobReturn["Result"] = "OK";
                    jobReturn["ReturnMessage"] = $"차량번호: {carNum} 할인권 적용완료";
                    jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                }
            }
            else
            {
                jobReturn["Result"] = "OK";
                jobReturn["ReturnMessage"] = "할인권 적용완료";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
            }

            return jobReturn;
        }

       

        private static int GetTotalParkingMinutes(string inputMsg)
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

                var discountText = await cells[1].InnerTextAsync();

                if (discountText.Contains("8000")) // "8000 원" 포함된 경우
                {
                    var cancelButton = await row.QuerySelectorAsync("button[id^='delete']");
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
        /// <param name="totalRealParkingMinutes">전체 주차시간</param>
        /// <param name="alreadyDiscountedMinutes">이미 사전 할인받은 금액</param>
        /// <param name="inventory">주차권 종류 및 수량</param>
        /// <param name="bufferMinutes">출차 보장 시간</param>
        /// <returns></returns>
        public static ParkingDiscountPlan ApplyDiscountTicketsWithInventory(int realFee, int totalRealParkingMinutes, int alreadyDiscountedMinutes, DiscountInventory inventory, int bufferMinutes = 15)
        {
            const int feePerBlock = 2000;
            const int minutesPerBlock = 30;

            // 1. 요금 기준 최대 커버 시간
            int feeBlocks = (int)Math.Ceiling(realFee / (double)feePerBlock);
            int maxMinutesByFee = feeBlocks * minutesPerBlock;

            // 2. 실제 필요한 추가 할인 시간 (실주차 - 기존 할인 + 여유시간)
            int requiredMinutes = Math.Max(0, (totalRealParkingMinutes - alreadyDiscountedMinutes) + bufferMinutes);

            // 3. 실제 할인 적용할 시간 = 요금 기준 한도 vs 실제 필요한 추가 시간 중 작은 값
            int targetMinutes = Math.Min(requiredMinutes, maxMinutesByFee);

            var result = new ParkingDiscountPlan();

            // 4. 그리디 적용(가장 큰 단위부터 적용)
            int use4h = Math.Min(targetMinutes / 240, inventory.Count4Hour);
            result.Use4Hour = use4h;
            targetMinutes -= use4h * 240;

            int use1h = Math.Min(targetMinutes / 60, inventory.Count1Hour);
            result.Use1Hour = use1h;
            targetMinutes -= use1h * 60;

            int use30m = Math.Min(targetMinutes / 30, inventory.Count30Min);
            result.Use30Min = use30m;
            targetMinutes -= use30m * 30;

            result.UncoveredMinutes = targetMinutes;

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
            try { await _browser?.CloseAsync(); } catch { }
            _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
            _context = await _browser.NewContextAsync();
        }

    }

}