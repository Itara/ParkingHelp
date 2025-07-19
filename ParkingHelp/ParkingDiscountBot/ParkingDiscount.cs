using log4net;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.SlackBot;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParkingHelp.ParkingDiscountBot
{
    public class ParkingDiscount
    {
        private readonly SlackNotifier SlackNotifier;
        public ParkingDiscount(SlackNotifier _slackNotifier)
        {
            SlackNotifier = _slackNotifier;
        }
        public async Task<JObject> RegisterParkingDiscountAsync(string carNumber, bool notifySlackChannel = false)
        {
            JObject jobReturn = new JObject
            {
                ["Result"] = "Fail",
                ["ReturnMessage"] = "Unknown Error"
            };
            try
            {
                using var playwright = await Playwright.CreateAsync();
                var browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = true,
                    SlowMo = 50,
                    Args = new[]
                    {
                       "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage"
                    }
                });

                var context = await browser.NewContextAsync();
                var page = await context.NewPageAsync();

                Console.WriteLine("로그인 페이지 이동 중...");
                await page.GotoAsync("http://gidc001.iptime.org:35052/nxpmsc/login", new()
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                });
                await page.ScreenshotAsync(new() { Path = "crash_check.png" });
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
                        string feeValueRaw = await feeElement.InputValueAsync(); // 예: "0 원"

                        // 3. 숫자만 추출 (공백, 원 제거)
                        string numericPart = System.Text.RegularExpressions.Regex.Replace(feeValueRaw, @"[^0-9]", "");
                        int feeValue = int.Parse(numericPart);

                        // 4. 주차금액이 0보다 크면 방문자주차권 버튼 클릭
                        if (feeValue > 0)
                        {
                            var now = DateTime.Now;
                            var today = now.DayOfWeek;

                            string alertMessage = "";
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

                            var discountButton = page.Locator("#add-discount-0");

                            if (isHoliday)
                            {
                                discountButton = page.Locator("#add-discount-1");
                            }

                            if (await discountButton.IsVisibleAsync())
                            {
                                Console.WriteLine($"할인권을 적용합니다!");
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
                                    return jobReturn;
                                }

                                await page.WaitForFunctionAsync(
                                  "(prev) => document.querySelector('#realFee')?.value !== prev",
                                    feeValueRaw, // 최대 5초 기다림
                                    new() { Timeout = 5000 }
                                );

                                // 금액 다시 확인
                                string feeValueAfterRaw = await page.Locator("#realFee").InputValueAsync();
                                int feeValueAfter = int.Parse(Regex.Replace(feeValueAfterRaw, @"[^0-9]", ""));
                                Console.WriteLine($"할인권 적용 후 주차금액: {feeValue} -> {feeValueAfter}원");

                                jobReturn["Result"] = "OK";
                                jobReturn["ReturnMessage"] = $"할인권 적용 후 주차금액: {feeValue}원 => {feeValueAfter}원";

                            }
                            else
                            {
                                Console.WriteLine("ID '#add-discount-0' 버튼이 존재하지 않음. 텍스트로 재시도");

                                // 텍스트 기반 선택자 fallback
                                var fallbackButton = page.Locator($"button:has-text('{discountButtonText}')");
                                if (await fallbackButton.IsVisibleAsync())
                                {
                                    Console.WriteLine("텍스트 기반 방문자주차권 버튼 존재. 클릭 시도.");
                                    await fallbackButton.ClickAsync();

                                    jobReturn["Result"] = "OK";
                                    jobReturn["ReturnMessage"] = "주차금액이 0원이므로 할인권 적용 생략";
                                    Console.WriteLine("주차금액이 0원이므로 할인권 적용 생략");
                                }
                                else
                                {
                                    Console.WriteLine("방문자주차권 버튼을 찾을 수 없습니다.");
                                    jobReturn["Result"] = "Fail";
                                    jobReturn["ReturnMessage"] = "방문자주차권 버튼을 찾을 수 없습니다.";
                                }
                            }
                        }
                        else
                        {
                            jobReturn["Result"] = "OK";
                            jobReturn["ReturnMessage"] = "주차금액이 0원이므로 할인권 적용 생략";
                            await SlackNotifier.SendMessageAsync($"차량번호:[{carNum}]는 주차요금이 0원입니다. ", null);
                            Console.WriteLine("주차금액이 0원이므로 할인권 적용 생략");
                        }

                        if (notifySlackChannel && jobReturn["Result"]!.ToString() == "OK")
                        {
                            await SlackNotifier.SendMessageAsync($"차량번호:[{carNum}] 방문자 주차권이 적용되었습니다.{jobReturn["ReturnMessage"]}", null);
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
                            }
                        };
                    }
                    else if (carNoList.Count < 1)
                    {
                        jobReturn = new JObject
                        {
                            ["Result"] = "Fail",
                            ["ReturnMessage"] = $"차량번호:{carNumber}는 미등록 차량입니다."
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                jobReturn["ReturnMessage"] = ex.Message;
            }
            return jobReturn;
        }
    }
}