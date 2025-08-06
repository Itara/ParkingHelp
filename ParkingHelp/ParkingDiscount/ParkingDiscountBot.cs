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

                if (File.Exists(SessionFilePath))
                {
                    _context = await _browser.NewContextAsync(new()
                    {
                        StorageStatePath = SessionFilePath,
                        ViewportSize = null,
                        BypassCSP = true,
                        IgnoreHTTPSErrors = true
                    });
                }
                else
                {
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

                    // 세션 저장
                    string storageStateJson = await _context.StorageStateAsync();
                    await File.WriteAllTextAsync(SessionFilePath, storageStateJson);
                    await page.CloseAsync();
                }

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
                            DiscountJobType.ApplyDiscount => await RunDiscountAsync(parkingDiscountModel.CarNumber,parkingDiscountModel.IsGetOffWorkTime),
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
                        _ =Task.Run(() => slackNotifier.SendDMAsync($"차량번호: {carNumber} 할인권 적용 완료", userId));
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
        private static async Task<JObject> RunDiscountAsync(string carNumber,bool isGetOffWork)
        {
            var page = await _context.NewPageAsync(); //신규 페이지 생성 후 page객체 전송
            await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/discount-search/original", new()
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            var result = await RegisterParkingDiscountAsync(carNumber, page, isGetOffWork);
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
        /// <param name="isGetOffWork">현재 퇴근 여부(True = 현재시간 계산 False = 퇴근시간(6시) 기준)</param>
        /// <returns></returns>
        public static async Task<JObject> RegisterParkingDiscountAsync(string carNumber, IPage page, bool notifySlackChannel = false,bool isGetOffWork = false)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error",
                ["ResultType"] = Convert.ToInt32(DisCountResultType.Error)
            };
            try
            {
                // 로그인 후 URL 또는 특정 요소 대기 (필요시 수정)
                //차량번호 텍스트박스 입력
                await page.FillAsync("#carNo", $"{carNumber}");
                await page.ClickAsync("#btnCarSearch");
                await page.WaitForSelectorAsync("#searchDataTable tbody tr");

                //차량번호와 입차시간 추출
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

                        jobReturn = await ApplyDiscount(feeValue, carNum, page, jobReturn,isGetOffWork);
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
                            ["Result"] = "OK",
                            ["ReturnMessage"] = $"차량번호 {carNumber}는 입차 차량이 아닙니다.",
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
                await page.CloseAsync();
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
        /// <param name="isGetOffWork">현재 퇴근 여부(현재시간 계산 or 퇴근시간 기준)</param>
        /// <returns>jobReturn</returns>
        private static async Task<JObject> ApplyDiscount(int feeValue, string carNum, IPage page, JObject jobReturn, bool isGetOffWork = false)
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
                    totalParkingMinute = GetTotalParkingMinutes(parkingTimeText, isGetOffWork);
                }

                if(totalParkingMinute != -1)
                {
                    try
                    {
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
                    Logs.Info($"차량번호: {carNum} 할인권 적용완료.");
                }
                else
                {
                    jobReturn["Result"] = "OK";
                    jobReturn["ReturnMessage"] = $"차량번호: {carNum} 할인권 적용을 했지만 잔액 존재";
                    jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.SuccessButFee);
                    Logs.Info($"차량번호: {carNum} 할인권 적용했지만 잔액이 존재함.");
                }
            }
            else
            {
                jobReturn["Result"] = "OK";
                jobReturn["ReturnMessage"] = $"{carNum} 기본 할인권 적용완료";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                Logs.Info($"차량번호: {carNum} 기본 할인권 적용완료");
            }
            return jobReturn;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputMsg">주차시간(0일 7시 5분) </param>
        /// <param name="isGetOffWork">퇴근여부 True = 퇴근 False = 배치 동작시간</param>
        /// <returns></returns>
        private static int GetTotalParkingMinutes(string inputMsg, bool isGetOffWork = false )
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

            //이후 남은 시간이 15분 이상이고 30분 할인권이 남아있으면 30분 할인권을 추가로 적용
            if (targetMinutes > 15 && inventory.Count30Min > result.Use30Min) 
            {
                result.Use30Min += 1;
                targetMinutes -= 30;
                if (targetMinutes < 0)
                    targetMinutes = 0;
            }
            //3.에서 선택하고 남은 잔여시간
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