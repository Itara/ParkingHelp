using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Logging;
namespace ParkingHelp.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AutomationController : ControllerBase
{
    private readonly AppDbContext _context;
    public AutomationController(AppDbContext context)
    {
        _context = context; // 세션 저장은 대기
    } 
    private const string LoginUrl = "http://gidc001.iptime.org:35052/nxpmsc/login";
    private const string UserId = "C2115";
    private const string Password = "6636";

    /// <summary>
    /// 주차장 시스템에 로그인하여 차량 정보를 조회하고 할인권을 자동 적용한 후
    /// 무료 주차 가능한 남은 시간(분)을 계산하여 반환
    /// </summary>
    /// <param name="param">차량번호가 포함된 파라미터</param>
    /// <returns>무료 주차 가능한 남은 시간(분)</returns>
    [HttpGet()]
    public async Task<IActionResult> Login([FromQuery] MemberGetParam param)
    {
        try
        {
            // Playwright 브라우저 인스턴스 생성 (헤드리스 모드 비활성화)
            using var pw = await Playwright.CreateAsync();
            await using var browser = await pw.Chromium.LaunchAsync(new() 
            {
                Headless = false
            });
            var ctx = await browser.NewContextAsync();
            var page = await ctx.NewPageAsync();

            // 브라우저 다이얼로그 자동 승인 설정
            page.Dialog += async (_, d) => await d.AcceptAsync();

            // 1. 주차장 관리 시스템 로그인
            await page.GotoAsync(LoginUrl);
            await page.FillAsync("#id", UserId);           // 사용자 ID 입력
            await page.FillAsync("#password", Password);   // 비밀번호 입력
            await page.ClickAsync("#loginBtn");            // 로그인 버튼 클릭

            // 2. 차량번호로 주차 정보 검색
            await page.WaitForSelectorAsync("#carNo");     // 차량번호 입력
            await page.FillAsync("#carNo", param.carNumber);  // 차량번호 입력
            await page.ClickAsync("#btnCarSearch");           // 검색 버튼 클릭

            // 검색 결과가 없는 경우 에러 처리
            var empty = await page.QuerySelectorAsync(".dataTables_empty");
            if (empty != null && (await empty.InnerTextAsync()).Contains("데이터가 없습니다"))
                return BadRequest(new { success = false, message = "데이터가 없습니다" });

            // 3. 차량 상세 정보 페이지로 이동
            var carLink = await page.QuerySelectorAsync("a[onclick^='carDetail2']");
            if (carLink is null)
            {
                Logs.Info("차량 상세 링크를 찾을 수 없습니다.");
            }

            await carLink.ClickAsync();                    // 차량 상세 링크 클릭
            await page.WaitForSelectorAsync("#startDate"); // 입차시간 필드 로딩 대기

            // 4. 입차시간 정보 파싱 및 검증
            var inTimeText = await page.Locator("#startDate").InputValueAsync();
            if (!DateTime.TryParse(inTimeText, out var inTime))
            {
                Logs.Info("입차시간 파싱 실패");
            }

            // 5. 요금 파싱
            var now = DateTime.Now;
            int ParseFee(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return 0;
                raw = raw.Replace(",", "").Replace("원", "").Trim();
                return int.TryParse(raw, out var fee) ? fee : 0;
            }

            // 현재 주차요금을 가져오기
            async Task<int> GetFeeAsync() => ParseFee(await page.Locator("#realFee").InputValueAsync());
            const int feePer30 = 2000; // 30분당 기본 요금
            int totalMinutes = Math.Max(0, (int)(DateTime.Now - inTime).TotalMinutes);

            // 6. 방문주차권 자동 적용
            // 주말, 평일 구분하여 방문주차권 사용
            bool isWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            string[] visitorSelectors = isWeekend
                ? new[] { "#add-discount-1", "button:has-text(\"방문\"):has-text(\"주말\")", "button:has-text(\"방문주차권\"):has-text(\"주말\")" }
                : new[] { "#add-discount-0", "button:has-text(\"방문\"):has-text(\"평일\")", "button:has-text(\"방문주차권\"):has-text(\"평일\")" };
            int appliedVisitorTickets = 0; // 적용된 방문주차권 수량
            int visitorDiscountTotal = 0;  // 방문주차권으로 인한 총 할인 금액

            // 방문주차권 최대 2장까지 적용 시도
            for (int i = 0; i < 2; i++)
            {
                ILocator? btn = null;
                foreach (var sel in visitorSelectors)
                {
                    var cand = page.Locator(sel).First;
                    try
                    {
                        if (await cand.CountAsync() > 0)
                        {
                            btn = cand;
                            break;
                        }
                    }
                    catch(Exception ex)
                    {
                        Logs.Info($"오류 발생{ex.Message}");
                    }
                }

                if (btn is null) break; // 더 이상 적용 가능한 방문주차권 없음

                // 방문주차권 적용 전 현재 요금 기록
                int feeBefore = await GetFeeAsync();
                await btn.ScrollIntoViewIfNeededAsync();  // 버튼이 화면에 보이도록 스크롤
                await btn.ClickAsync();                   // 방문주차권 적용 버튼 클릭
                await page.WaitForTimeoutAsync(300);

                bool changed = false;
                for (int t = 0; t < 10; t++) // 일단 10번 테스트
                {
                    await page.WaitForTimeoutAsync(300);
                    int feeAfter = await GetFeeAsync();
                    if (feeAfter != feeBefore)
                    {
                        visitorDiscountTotal += 8000; // 방문주차권 1장당 8000원 할인
                        appliedVisitorTickets++;
                        changed = true;
                        break;
                    }
                }
                // 이미 방문주차권이 적용된 상태에서 다시 클릭한 경우
                if (!changed)
                {
                    visitorDiscountTotal += 8000;
                    appliedVisitorTickets++;
                }
            }

            // 7. 추가 할인 쿠폰 자동 적용
            var couponDefs = new[]
            {
                new { price = 16000, selectors = new[]{ "button:has-text(\"4시간\")" } },  // 4시간 쿠폰 (16000원)
                new { price = 4000,  selectors = new[]{ "button:has-text(\"1시간\")" } },  // 1시간 쿠폰 (4000원)
                new { price = 2000,  selectors = new[]{ "button:has-text(\"30분\")" } },   // 30분 쿠폰 (2000원)
            };

            int additionalDiscount = 0;  // 총 할인 금액

            int feeNow = await GetFeeAsync(); // 현재 요금

            // 각 쿠폰별로 최대한 많이 사용
            foreach (var def in couponDefs)
            {
                while (true)
                {
                    int feeBefore = await GetFeeAsync();
                    if (feeBefore <= 0) break; // 더 이상 낼 요금이 없으면 종료
                                                // 
                    int remainingSlots = (int)Math.Ceiling(feeBefore / (double)feePer30); // 현재 요금이 쿠폰 적용 가능 금액보다 작으면 종료
                    int couponSlots = def.price / feePer30; // 쿠폰 적용 가능 슬롯 계산

                    if (couponSlots > remainingSlots) break; // 쿠폰 적용 가능 여부 확인

                    // 현재 쿠폰 버튼 찾기
                    ILocator? btn = null;
                    foreach (var sel in def.selectors)
                    {
                        var cand = page.Locator(sel).First;
                        if (await cand.CountAsync() > 0)
                        {
                            btn = cand;
                            break;
                        }
                    }
                    if (btn is null) break; // 해당 쿠폰 버튼을 찾을 수 없음

                    // 쿠폰 적용
                    await btn.ScrollIntoViewIfNeededAsync();
                    await btn.ClickAsync();
                    await page.WaitForTimeoutAsync(300);

                    // 요금 변경 확인 및 할인 금액 계산
                    bool changed = false;
                    for (int t = 0; t < 10; t++)
                    {
                        await page.WaitForTimeoutAsync(300);
                        int feeAfter = await GetFeeAsync();
                        if (feeAfter != feeBefore)
                        {
                            feeNow = feeAfter;
                            additionalDiscount += feeBefore - feeAfter; // 실제 할인된 금액 누적
                            changed = true;
                            break;
                        }
                    }

                    if (!changed) break; // 요금이 변하지 않으면 더 이상 적용 불가
                    if (feeNow <= 0) break; // 요금이 0원이 되면 종료
                }

                if (feeNow <= 0) break; // 전체 요금이 0원이 되면 모든 쿠폰 적용 종료
            }

            // 8.주차 시간 계산
            // 계산 방식 1: 직접 계산 (방문주차권 + 추가 할인 쿠폰)
            var inTimeText2 = await page.Locator("#startDate").InputValueAsync(); // 시작시간
            DateTime.TryParse(inTimeText2, out var inTime2);
            int finalTotalMinutes = Math.Max(0, (int)(DateTime.Now - inTime2).TotalMinutes); // 최종 주차 시간
            int actualCoveredMinutes1 = appliedVisitorTickets * 120 + (int)Math.Floor((double)additionalDiscount / feePer30 * 30);// 실제 무료 주차 시간 계산 (방문주차권 2장 + 추가 할인 쿠폰)
            int minutesUntilPay1 = Math.Max(0, actualCoveredMinutes1 - finalTotalMinutes); // 무료 주차 가능한 남은 시간

            // 계산 방식 2: 요금 계산 (장기 주차용 - 4시간 이상)
            // 현재 주차시간으로 계산된 기본요금과 실제 최종요금의 차이로 할인시간 계산
            int baseFee = (int)Math.Ceiling(finalTotalMinutes / 30.0) * feePer30; // 할인 전 기본 요금
            int finalFee = await GetFeeAsync(); // 할인 후 최종 요금
            int totalDiscount2 = Math.Max(0, baseFee - finalFee); // 총 할인 금액
            int coveredMinutes2 = (int)Math.Floor(totalDiscount2 / (double)feePer30 * 30); // 할인 금액을 시간으로 변경
            int minutesUntilPay2 = Math.Max(0, coveredMinutes2 - finalTotalMinutes); // 무료 주차 가능한 남은 시간

            // 주차 시간에 따라 더 정확한 계산 방식 선택
            int minutesUntilPay = finalTotalMinutes < 240 ? minutesUntilPay1 : minutesUntilPay2; // 4시간(240분) 미만: 방식1 (직접 계산), 4시간 이상: 방식2 (요금 기반)
            
            return Ok(new { minutesUntilPay }); // 9. 무료 주차 가능한 남은 시간을 분 단위로 반환
        }
        catch (Exception ex)
        {
            // 오류 발생 시 에러 메시지 반환
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}