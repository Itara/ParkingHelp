using log4net;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.Models;
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
        public ParkingDiscountResultEventArgs(string carNumber,string memberEmail, JObject result)
        {
            CarNumber = carNumber;
            Result = result;
            MemberEmail = memberEmail;
        }
    }   
    public static class PlaywrightManager
    {
        //주차할인권 적용 페이지에 접근할 수 있는 Playwright 객체
        private static IPlaywright _playwright;
        private static IBrowser _browser;
        private static IBrowserContext _context;  // 전역 context 추가
        //작업큐 관련
        private static PriorityQueue<(string carNumber ,string memberEmail, DiscountJobType jobType, TaskCompletionSource<JObject> tcs), int> _ParkingDiscountPriorityQueue = new();
        private static SemaphoreSlim _semaphore = new(0);
        private static object _lock = new();

        private static TimeOnly _AutoDisCountApplyTime = new TimeOnly(17,0,0); // 자동 할인권 적용 시간 (17시 기준)
        //설정 및 의존성 주입 객체
        private static IConfiguration _config;
        private static IServiceProvider _services;

        private static AppDbContext _DbContext = null; // DB Context
                                                       //슬랙 채널에 결과 알림
        private static SlackOptions slackOptions = null;
        private static SlackNotifier slackNotifier = new SlackNotifier(slackOptions);

        public static event EventHandler<ParkingDiscountResultEventArgs>? OnParkingDiscountEvent; // 에러 발생시 이벤트

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
                while (true)
                {
                    //배치시작시간
                    TimeOnly currentTime = TimeOnly.FromDateTime(DateTime.Now);
                    if (TimeOnly.TryParse(_config["AutoDiscountTime"], out _AutoDisCountApplyTime) && _AutoDisCountApplyTime.Hour == currentTime.Hour && _AutoDisCountApplyTime.Minute == currentTime.Minute)
                    {
                        List<MemberDto> members = GetMemberList();
                        foreach (MemberDto meber in members)
                        {
                            foreach (var car in meber.Cars)
                            {
                                //자동 할인권 적용 작업큐에 추가
                                string memberEmail = meber.Email ?? string.Empty;
                                _ = EnqueueAsync(car.CarNumber, DiscountJobType.ApplyDiscount, 100 , memberEmail);
                            }
                        }
                    }

                    if (_ParkingDiscountPriorityQueue.Count == 0)
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    await _semaphore.WaitAsync();
                    (string carNumber, string memberEmail, DiscountJobType jobType, TaskCompletionSource<JObject> tcs) item;
                   
                    lock (_lock)
                    {
                        //PriorityQueue로 생성해서 우선순위가 높은것부터 뽑아옴
                        //우선순위
                        // 1. API로 호출된 차량 (High)
                        // 2. 퇴근 등록을 한 차량 (Medium)
                        // 3. 배치 시간이 되서 작업시간이 된 차량 (Low)
                        _ParkingDiscountPriorityQueue.TryDequeue(out item ,out int priority);  
                    }

                    try
                    {
                        var result = item.jobType switch
                        {
                            DiscountJobType.ApplyDiscount => await RunDiscountAsync(item.carNumber),
                            DiscountJobType.CheckFeeOnly => await RunCheckFeeAsync(item.carNumber),
                            _ => new JObject { ["Result"] = "Fail", ["ReturnMessage"] = "Unknown Job Type" }
                        };
                        item.tcs.SetResult(result); 
                        OnParkingDiscountEvent?.Invoke(null, new ParkingDiscountResultEventArgs(item.carNumber, item.memberEmail, result)); //슬랙채널에 결과를 알리기 위해 이벤트 호출
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

        private static void PlaywrightManager_OnParkingDiscountEvent(object? sender, ParkingDiscountResultEventArgs e)
        {
           string carNumber = e.CarNumber;
           Console.WriteLine($"차량번호: {carNumber} 할인권 적용 결과: {e.Result["Result"]} Message : {e.Result["ReturnMessage"]} {e.MemberEmail}");
           _ =Task.Run(async () =>
           {
               
               await slackNotifier.SendMessageAsync($"차량번호: {carNumber} 할인권 적용 결과: {e.Result["Result"]} Message : {e.Result["ReturnMessage"]}", e.MemberEmail);
           });
        }

        public static List<MemberDto> GetMemberList()
        {
            //db객체 가져옴
            using var scope = _services.CreateScope();
            _DbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            List<MemberDto> members = _DbContext.Members
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

            return members;
        }

        /// <summary>
        /// 할인권 적용 작업큐에 신규 리스트 추가
        /// </summary>
        /// <param name="carNumber"></param>
        /// <param name="jobType">주차요금 조회 or 할인권 조회</param>
        /// <param name="priority">실행 우선순위 (0이 될수록 우선순위 증가 즉시 할인권 적용하려면 낮게 설정)</param>
        /// <returns></returns>
        public static Task<JObject> EnqueueAsync(string carNumber, DiscountJobType jobType, int priority = 100 , string memberEmail = "")
        {
            var tcs = new TaskCompletionSource<JObject>(); // 작업 완료를 기다리는 Task 생성

            lock (_lock) 
            {
                Console.WriteLine($"할인권 적용 요청을 받았습니다. 현재 할인권을 적용해야할 List는 총 {_ParkingDiscountPriorityQueue.Count}개 입니다");
                _ParkingDiscountPriorityQueue.Enqueue((carNumber,memberEmail, jobType, tcs), priority);
            }
            _semaphore.Release();

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

        public static async Task<JObject> RegisterParkingDiscountAsync(string carNumber, IPage page, bool notifySlackChannel = false)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error",
                ["ResultType"] =Convert.ToInt32(DisCountResultType.Error)
            };
            try
            {
                Console.WriteLine("로그인 페이지 이동 중...");
                if(page.Url.Contains("login"))

               Console.WriteLine("로그인 필요 → 로그인 시작");

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

                        // 4. 주차금액이 0보다 크면 방문자주차권 버튼 클릭
                        if (feeValue > 0)
                        {
                            jobReturn = await ApplyDiscount(feeValue, carNum, page, jobReturn);
                        }
                        else
                        {
                            jobReturn["Result"] = "OK";
                            jobReturn["ReturnMessage"] = "주차금액이 0원이므로 할인권 적용 생략";
                            jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.NotFee);
                            Console.WriteLine("주차금액이 0원이므로 할인권 적용 생략");
                        }
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
                            ["ResultType"] = Convert.ToInt32(DisCountResultType.MoreThanTwoCar)
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

        private static async Task<JObject> ApplyDiscount(int feeValue, string carNum, IPage page , JObject jobReturn)
        {
            var cancelButtons = await page.QuerySelectorAllAsync("button[id^='delete']");
            if (cancelButtons.Count == 2)
            {
                jobReturn["Result"] = "Fail";
                jobReturn["ReturnMessage"] = $"차량번호:{carNum} 할인권이 이미 적용";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.AlreadyUse);
            }
            var now = DateTime.Now;
            var today = now.DayOfWeek;

            string alertMessage = "";
            //할인권 적용했을때 결과메세지 수신이벤트
            var dialogTcs = new TaskCompletionSource<IDialog>();
            page.Dialog += (_, dialog) =>
            {
                alertMessage = dialog.Message;
                dialog.AcceptAsync();
                dialogTcs.TrySetResult(dialog);
            };

            // 휴일 여부 판단 (일요일 or 공휴일)
            bool isHoliday = today == DayOfWeek.Sunday || today == DayOfWeek.Saturday;
            string discountButtonText = isHoliday ? "방문자주차권(휴일)" : "방문자주차권";

            var discountButton = page.Locator("#add-discount-0"); //방문자주차권 (기본)

            if (isHoliday)
            {
                discountButton = page.Locator("#add-discount-1"); //방문자주차권 (휴일용)
            }

            /*나머지 주차권
                var discount30MinuteButton = page.Locator("#add-discount-2"); //방문자주차권 (30분)
                var discount1HourButton = page.Locator("#add-discount-3"); //방문자주차권 (1시간)
                var discount4HourButton = page.Locator("#add-discount-4"); //방문자주차권 (4시간)
                var discountAllDayButton = page.Locator("#add-discount-5"); //방문자주차권 (일일권)
            */

            if (await discountButton.IsVisibleAsync())
            {
                await discountButton.ClickAsync();

                // 3. 실제 Dialog가 나타날 때까지 기다림 (최대 3초)
                var dialogTask = dialogTcs.Task;
                if (await Task.WhenAny(dialogTask, Task.Delay(5000)) == dialogTask)
                {
                    var dialog = await dialogTask;
                }
                if (alertMessage.Contains("불가능"))
                {
                    jobReturn["Result"] = "Fail";
                    jobReturn["ReturnMessage"] = $"차량번호:{carNum} 할인권 적용 실패 :{alertMessage}";
                    jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.AlreadyUse);
                    return jobReturn;
                }

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
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Success);
                //할인권 더 적용
                if (feeValueAfter > 0 )
                {
                    cancelButtons = await page.QuerySelectorAllAsync("button[id^='delete']");
                    if(cancelButtons.Count == 2)
                    {
                        jobReturn["Result"] = "Fail";
                        jobReturn["ReturnMessage"] = $"차량번호:{carNum} 할인권이 이미 적용";
                        jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.AlreadyUse);
                        return jobReturn;
                    }
                    else
                    {
                        //잔액이 남았으므로 추가 할인권 적용
                        await discountButton.ClickAsync();
                        dialogTask = dialogTcs.Task;
                        
                        if (await Task.WhenAny(dialogTask, Task.Delay(5000)) == dialogTask)
                        {
                            var dialog = await dialogTask;
                        }
                        if (alertMessage.Contains("불가능"))
                        {
                            jobReturn["Result"] = "Fail";
                            jobReturn["ReturnMessage"] = $"차량번호:{carNum} 할인권 적용 실패 :{alertMessage}";
                            jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.AlreadyUse);
                            return jobReturn;
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("방문자주차권 버튼을 찾을 수 없습니다.");
                jobReturn["Result"] = "Fail";
                jobReturn["ReturnMessage"] = "방문자주차권 버튼을 찾을 수 없습니다.";
                jobReturn["ResultType"] = Convert.ToInt32(DisCountResultType.Error);

            }
            return jobReturn;
        }

        private static async Task RestartBrowserAsync()
        {
            try { await _browser?.CloseAsync(); } catch { }
            _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
            _context = await _browser.NewContextAsync();
        }

    }

}