using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using ParkingHelp.Logging;
using System.Text.RegularExpressions;

namespace ParkingHelp.Services.ParkingDiscount
{
    /// <summary>
    /// 주차 할인 자동화 처리 클래스
    /// Playwright를 사용하여 웹 브라우저를 자동화하여 주차 할인을 적용하고 계산합니다.
    /// </summary>
    public class ParkingAutomation
    {
        // 브라우저 컨텍스트 동시 사용 제한 (최대 10개)
        private static readonly SemaphoreSlim _contextSemaphore = new(10, 10);

        // Playwright 인스턴스 (브라우저 자동화 도구)
        private static IPlaywright? _playwright;

        // 브라우저 인스턴스 (실제 브라우저 프로세스)
        private static IBrowser? _browser;

        // 초기화 작업 동기화를 위한 락 객체
        private static readonly object _initLock = new();

        // 브라우저 초기화 완료 여부
        private static bool _initialized = false;

        // 브라우저 초기화 실패 여부
        private static bool _initializationFailed = false;

        // 로그인 세션 상태를 저장할 파일 경로
        private static readonly string SessionFilePath = Path.Combine(AppContext.BaseDirectory, "session.json");
        private const string LoginUrl = "http://gidc001.iptime.org:35052/nxpmsc/login";
        private const string UserId = "C2115";
        private const string Password = "6636";

        /// <summary>
        /// 생성자 - 백그라운드에서 브라우저 초기화 시작
        /// </summary>
        public ParkingAutomation()
        {
            // 비동기로 브라우저 초기화 작업을 백그라운드에서 실행
            _ = Task.Run(InitializeBrowserAsync);
        }

        /// <summary>
        /// 주차 할인 처리 메인 메서드
        /// 차량번호를 받아서 할인을 적용하고 무료 주차 가능 시간을 반환
        /// </summary>
        /// <param name="carNumber">조회할 차량번호</param>
        /// <returns>처리 결과가 담긴 JSON 객체</returns>
        public async Task<JObject> ProcessParkingForApiAsync(string carNumber)
        {
            // 브라우저 컨텍스트와 페이지 변수 초기화
            IBrowserContext? ctx = null;
            IPage? page = null;

            try
            {
                // 차량번호
                if (string.IsNullOrWhiteSpace(carNumber))
                    return CreateErrorResult("차량번호가 입력되지 않았습니다");

                // 브라우저 초기화 확인
                if (!await EnsureBrowserInitializedAsync())
                    return CreateErrorResult("브라우저 초기화 실패");

                // 새로운 브라우저 컨텍스트 생성
                ctx = await CreateNewContextAsync();
                if (ctx == null) return CreateErrorResult("브라우저 리소스를 가져올 수 없습니다");

                // 새 페이지 생성
                page = await ctx.NewPageAsync();

                // 페이지에서 발생하는 다이얼로그(alert, confirm 등)를 자동으로 확인 처리
                page.Dialog += async (_, d) => await d.AcceptAsync();

                // 로그인
                await EnsureLoginAsync(page);

                // 차량 검색 및 상세 정보 조회
                if (!await SearchCarAsync(page, carNumber))
                    return CreateErrorResult("차량 정보를 찾을 수 없습니다");

                // 입차시간 정보 가져오기 및 파싱
                var inTimeText = await page.Locator("#startDate").InputValueAsync();
                if (string.IsNullOrWhiteSpace(inTimeText) || !DateTime.TryParse(inTimeText, out var inTime))
                    return CreateErrorResult("입차시간 파싱 실패");

                // 할인 적용 및 최종 계산 수행
                return await ProcessDiscountAndCalculateTime(page, inTime);
            }
            catch (Exception ex)
            {
                // 예외 발생 시 로깅하고 에러 결과 반환
                Logs.Error($"주차 처리 오류: {ex}");
                return CreateErrorResult($"처리 오류: {ex.Message}");
            }
            finally
            {
                // 리소스 정리: 페이지 닫기
                if (page != null) try { await page.CloseAsync(); } catch { }

                // 리소스 정리: 브라우저 컨텍스트 해제
                await DisposeContextAsync(ctx);
            }
        }

        /// <summary>
        /// 로그인 처리 메서드
        /// 로그인 페이지로 이동하여 자동 로그인 수행
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        private static async Task EnsureLoginAsync(IPage page)
        {
            try
            {
                // 로그인 페이지로 이동
                await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });

                // 로그인
                if (page.Url.Contains("login"))
                {
                    await page.FillAsync("#id", UserId);
                    await page.FillAsync("#password", Password);
                    await page.ClickAsync("#loginBtn");
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"로그인 처리 오류: {ex}");
                throw new Exception($"로그인 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 차량 검색 및 상세 정보 조회 메서드
        /// 차량번호로 검색하여 해당 차량의 상세 정보 페이지로 이동
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="carNumber">검색할 차량번호</param>
        /// <returns>검색 성공 여부</returns>
        private static async Task<bool> SearchCarAsync(IPage page, string carNumber)
        {
            try
            {
                await page.FillAsync("#carNo", carNumber);
                await page.ClickAsync("#btnCarSearch");

                // 검색 결과가 없는 경우
                var empty = await page.QuerySelectorAsync(".dataTables_empty");
                if (empty != null && (await empty.InnerTextAsync()).Contains("데이터가 없습니다"))
                    return false;

                // 차량 상세 정보 링크 찾기
                var carLink = await page.QuerySelectorAsync("a[onclick^='carDetail2']");
                if (carLink == null) return false;

                // 차량 상세 정보 링크 클릭
                await carLink.ClickAsync();

                // 상세 페이지의 입차시간 필드가 로딩될 때까지 대기 (최대 1초)
                await page.WaitForSelectorAsync("#startDate", new() { Timeout = 1000 });

                return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"차량 검색 오류: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 할인 적용 및 최종 시간 계산 메서드
        /// 다양한 할인을 적용하고 무료 주차 가능 시간을 계산
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="inTime">입차 시간</param>
        /// <returns>처리 결과가 담긴 JSON 객체</returns>
        private async Task<JObject> ProcessDiscountAndCalculateTime(IPage page, DateTime inTime)
        {
            var now = DateTime.Now;
            // 주말 여부 확인
            bool isWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            /// <summary>
            /// 현재 주차 요금을 조회하는 내부 함수
            /// </summary>
            /// <returns>현재 주차 요금 (정수)</returns>
            async Task<int> GetFeeAsync()
            {
                try
                {
                    // 실제 요금 필드에서 값 가져오기
                    var raw = await page.Locator("#realFee").InputValueAsync();

                    // 값이 없는 경우 0 반환
                    if (string.IsNullOrWhiteSpace(raw)) return 0;

                    // 쉼표와 "원" 문자 제거 후 숫자만 추출
                    raw = raw.Replace(",", "").Replace("원", "").Trim();

                    // 문자열을 정수로 변환 (실패 시 0 반환)
                    return int.TryParse(raw, out var fee) ? fee : 0;
                }
                catch (Exception ex)
                {
                    Logs.Error($"요금 조회 오류: {ex}");
                    return 0;
                }
            }

            // 할인 적용 통계 변수들
            int appliedVisitorTickets = 0;      // 적용된 방문주차권 수
            int visitorDiscountTotal = 0;       // 방문주차권 총 할인액
            int additionalDiscount = 0;         // 추가 할인액 (쿠폰 등)

            // 1. 방문주차권 할인 적용 (평일/주말 구분)
            await ApplyVisitorTickets(page, GetFeeAsync, isWeekend, discount =>
            {
                visitorDiscountTotal += discount;
                appliedVisitorTickets++;
            });

            // 2. 추가 쿠폰 할인 적용 (4시간, 1시간, 30분 쿠폰)
            await ApplyAdditionalCoupons(page, GetFeeAsync, d => additionalDiscount += d);

            // 3. 할인 테이블에서 기존 할인 정보 처리
            await ProcessDiscountTable(page, d => additionalDiscount += d);

            // 4. 최종 무료 주차 가능 시간 계산
            var minutesUntilPay = await CalculateFinalTimeImproved(page, inTime, additionalDiscount, 2000);

            // 성공 결과 반환
            return new JObject
            {
                ["success"] = true,
                ["minutesUntilPay"] = minutesUntilPay,
            };
        }

        /// <summary>
        /// 방문주차권 할인 적용 메서드
        /// 평일/주말에 따라 적절한 방문주차권을 최대 2개까지 적용
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="getFeeAsync">현재 요금 조회 함수</param>
        /// <param name="isWeekend">주말 여부</param>
        /// <param name="onDiscountApplied">할인 적용 시 호출될 콜백 함수</param>
        private async Task ApplyVisitorTickets(IPage page, Func<Task<int>> getFeeAsync, bool isWeekend, Action<int> onDiscountApplied)
        {
            try
            {
                // 주말/평일에 따른 방문주차권 선택자 배열
                string[] visitorSelectors = isWeekend
                    ? new[] { "#add-discount-1", "button:has-text(\"방문\"):has-text(\"주말\")", "button:has-text(\"방문주차권\"):has-text(\"주말\")" }
                    : new[] { "#add-discount-0", "button:has-text(\"방문\"):has-text(\"평일\")", "button:has-text(\"방문주차권\"):has-text(\"평일\")" };

                // 최대 2개의 방문주차권 적용 시도
                for (int i = 0; i < 2; i++)
                {
                    ILocator? discountButton = null;

                    // 여러 선택자 중에서 사용 가능한 버튼 찾기
                    foreach (var selector in visitorSelectors)
                    {
                        try
                        {
                            // 선택자로 요소 찾기
                            var candidate = page.Locator(selector).First;

                            // 요소가 존재하는지 확인
                            if (await candidate.CountAsync() > 0)
                            {
                                discountButton = candidate;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 선택자 확인 실패 시 로깅하고 다음 선택자 시도
                            Logs.Info($"방문주차권 선택자 확인 오류: {ex.Message}");
                        }
                    }

                    // 사용 가능한 버튼이 없으면 중단
                    if (discountButton == null) break;

                    try
                    {
                        // 할인 적용 전 요금 조회
                        int feeBefore = await getFeeAsync();

                        // 방문주차권 버튼 클
                        if (!await SafeClickButton(page, discountButton))
                        {
                            Logs.Info("방문주차권 버튼 클릭 실패");
                            break;
                        }

                        // 클릭 후 짧은 대기
                        await Task.Delay(150);

                        // 요금 변경 확인
                        bool changed = false;
                        for (int t = 0; t < 8; t++)
                        {
                            await Task.Delay(150);
                            int feeAfter = await getFeeAsync();

                            // 요금이 변경되었으면 할인 적용 완료
                            if (feeAfter != feeBefore)
                            {
                                // 방문주차권 할인액 8000원으로 기록
                                onDiscountApplied(8000);
                                changed = true;
                                break;
                            }
                        }

                        // 요금 변경이 감지되지 않았어도 할인이 적용되었다고 가정
                        // (일부 경우에 UI 업데이트가 지연될 수 있음)
                        if (!changed)
                        {
                            onDiscountApplied(8000);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 방문주차권 적용 실패 시 로깅하고 중단
                        Logs.Info($"방문주차권 적용 오류: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // 전체 방문주차권 처리 실패 시 로깅
                Logs.Error($"방문주차권 처리 전체 오류: {ex}");
            }
        }

        /// <summary>
        /// 안전한 버튼 클릭 메서드
        /// 여러 번 시도하여 버튼 클릭의 안정성을 높임
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="button">클릭할 버튼 요소</param>
        /// <param name="maxRetries">최대 재시도 횟수</param>
        /// <returns>클릭 성공 여부</returns>
        private static async Task<bool> SafeClickButton(IPage page, ILocator button, int maxRetries = 3)
        {
            // 최대 재시도 횟수만큼 반복
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    // 1. 버튼이 보이는 상태가 될 때까지 대기 (최대 2초)
                    await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 });

                    // 2. 버튼이 화면에 보이도록 스크롤 (최대 3초)
                    await button.ScrollIntoViewIfNeededAsync(new() { Timeout = 3000 });

                    // 3. 짧은 대기로 UI 안정화
                    await Task.Delay(100);

                    // 4. 버튼 클릭 (최대 3초)
                    await button.ClickAsync(new() { Timeout = 3000 });

                    return true;
                }
                catch (Exception ex)
                {
                    Logs.Info($"버튼 클릭 시도 {retry + 1} 실패: {ex.Message}");

                    // 마지막 시도가 아니면 잠시 대기 후 재시도
                    if (retry < maxRetries - 1)
                        await Task.Delay(500);
                }
            }

            return false;
        }

        /// <summary>
        /// 최종 무료 주차 가능 시간 계산 메서드 (개선된 버전)
        /// 두 가지 계산 방식을 사용하여 더 정확한 결과 제공
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="inTime">입차 시간</param>
        /// <param name="additionalDiscount">추가 할인 시간(분)</param>
        /// <param name="feePer30">30분당 요금</param>
        /// <returns>무료 주차 가능 시간(분)</returns>
        private static async Task<int> CalculateFinalTimeImproved(IPage page, DateTime inTime, int additionalDiscount, int feePer30)
        {
            try
            {
                // 페이지에서 최신 입차시간 조회
                var inTimeText = await page.Locator("#startDate").InputValueAsync();
                DateTime.TryParse(inTimeText, out var finalInTime);

                // 파싱 실패 시 원본 입차시간 사용
                if (finalInTime == default) finalInTime = inTime;

                // 현재까지 주차한 총 시간(분) 계산
                int totalMinutes = Math.Max(0, (int)(DateTime.Now - finalInTime).TotalMinutes);

                // 방법 1: 할인 시간 기반 계산
                // 추가 할인 시간에서 현재 주차 시간을 뺀 값
                int minutesUntilPay1 = Math.Max(0, additionalDiscount - totalMinutes);

                // 페이지에서 현재 실제 요금 조회
                var feeText = await page.Locator("#realFee").InputValueAsync();
                if (string.IsNullOrWhiteSpace(feeText)) feeText = "0";

                // 실제 요금에서 숫자만 추출
                int finalFee = int.TryParse(Regex.Replace(feeText, @"[^0-9]", ""), out var f) ? f : 0;

                // 방법 2: 요금 기반 계산
                // 현재 주차 시간에 대한 기본 요금 계산 (30분 단위 올림)
                int baseFee = (int)Math.Ceiling(totalMinutes / 30.0) * feePer30;

                // 총 할인액 = 기본 요금 - 실제 요금
                int totalDiscount2 = Math.Max(0, baseFee - finalFee);

                // 할인액으로 커버되는 시간(분) 계산
                int coveredMinutes2 = (int)Math.Floor(totalDiscount2 / (double)feePer30 * 30);

                // 커버된 시간에서 현재 주차 시간을 뺀 값
                int minutesUntilPay2 = Math.Max(0, coveredMinutes2 - totalMinutes);

                // 주차 시간이 4시간(240분) 미만이면 방법1, 이상이면 방법2 사용
                // 4시간 이전에는 할인 시간 기반이 더 정확하고,
                // 4시간 이후에는 실제 요금 기반이 더 정확함
                return totalMinutes < 240 ? minutesUntilPay1 : minutesUntilPay2;
            }
            catch (Exception ex)
            {
                Logs.Error($"최종 시간 계산 오류: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 브라우저 초기화 메서드
        /// Playwright와 Chromium 브라우저를 초기화하고 세션을 설정
        /// </summary>
        private static async Task InitializeBrowserAsync()
        {
            // 이미 초기화되었으면 종료
            if (_initialized) return;

            // 동시에 여러 스레드에서 초기화하는 것을 방지
            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;  // 초기화 시작 표시
            }

            try
            {
                // Playwright 인스턴스 생성
                _playwright = await Playwright.CreateAsync();

                // Chromium 브라우저 실행 (헤드리스 모드, 보안 옵션 설정)
                _browser = await _playwright.Chromium.LaunchAsync(new()
                {
                    Headless = true,  // UI 없이 실행
                    Args = new[] {
                        "--no-sandbox",           // 샌드박스 비활성화 (서버 환경)
                        "--disable-gpu",          // GPU 비활성화 (서버 환경)
                        "--disable-dev-shm-usage" // /dev/shm 사용 비활성화 (메모리 절약)
                    }
                });

                // 기존 세션 검증 또는 새 세션 생성
                await ValidateOrCreateSessionAsync();
            }
            catch (Exception ex)
            {
                Logs.Error($"브라우저 초기화 실패: {ex}");
                _initialized = false;
                _initializationFailed = true;
            }
        }

        /// <summary>
        /// 브라우저 초기화 상태 확인 및 필요시 재초기화
        /// </summary>
        /// <returns>초기화 성공 여부</returns>
        private static async Task<bool> EnsureBrowserInitializedAsync()
        {
            // 이미 정상적으로 초기화되어 있고 실패하지 않았으면 성공
            if (_initialized && _browser != null && !_initializationFailed) return true;

            // 상태 리셋하고 재초기화 시도
            lock (_initLock) { _initialized = false; _initializationFailed = false; }
            await InitializeBrowserAsync();

            // 재초기화 결과 반환
            return _initialized && _browser != null && !_initializationFailed;
        }

        /// <summary>
        /// 브라우저 컨텍스트 기본 옵션 생성
        /// </summary>
        /// <param name="statePath">세션 상태 파일 경로 (선택사항)</param>
        /// <returns>브라우저 컨텍스트 옵션</returns>
        private static BrowserNewContextOptions DefaultContextOptions(string? statePath = null) => new()
        {
            StorageStatePath = statePath,    // 세션 상태 파일 경로
            BypassCSP = true,               // CSP(Content Security Policy) 우회
            IgnoreHTTPSErrors = true        // HTTPS 인증서 오류 무시
        };

        /// <summary>
        /// 새 브라우저 컨텍스트 생성
        /// 세마포어를 사용하여 동시 생성 수 제한
        /// </summary>
        /// <returns>생성된 브라우저 컨텍스트 또는 null</returns>
        private static async Task<IBrowserContext?> CreateNewContextAsync()
        {
            // 세마포어 획득 (동시 컨텍스트 수 제한)
            await _contextSemaphore.WaitAsync();
            try
            {
                // 브라우저가 없으면 null 반환
                if (_browser == null) return null;

                // 저장된 세션 상태를 사용하여 새 컨텍스트 생성
                return await _browser.NewContextAsync(DefaultContextOptions(SessionFilePath));
            }
            catch (Exception ex)
            {
                Logs.Error($"컨텍스트 생성 실패: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 브라우저 컨텍스트 해제
        /// 사용 완료된 컨텍스트를 정리하고 세마포어 해제
        /// </summary>
        /// <param name="context">해제할 컨텍스트</param>
        private static async Task DisposeContextAsync(IBrowserContext? context)
        {
            // 컨텍스트가 있으면 해제 시도
            if (context != null) try { await context.DisposeAsync(); } catch { }

            // 세마포어 해제 시도 (예외 발생해도 무시)
            try { _contextSemaphore.Release(); } catch { }
        }

        /// <summary>
        /// 기존 세션 검증 또는 새 세션 생성
        /// 저장된 세션이 유효한지 확인하고, 무효하면 새로 생성
        /// </summary>
        private static async Task ValidateOrCreateSessionAsync()
        {
            try
            {
                // 브라우저가 없으면 종료
                if (_browser == null) return;

                // 저장된 세션 상태로 임시 컨텍스트 생성
                var ctx = await _browser.NewContextAsync(DefaultContextOptions(SessionFilePath));
                var page = await ctx.NewPageAsync();

                // 로그인 페이지로 이동하여 세션 유효성 확인
                await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });

                // 로그인 페이지로 리다이렉트되면 세션이 만료된 것이므로 새 세션 생성
                if (page.Url.Contains("login"))
                    await CreateNewSessionAsync();

                // 임시 컨텍스트 정리
                await ctx.CloseAsync();
            }
            catch
            {
                // 검증 실패 시 새 세션 생성
                await CreateNewSessionAsync();
            }
        }

        /// <summary>
        /// 새로운 로그인 세션 생성 및 저장
        /// 자동 로그인을 수행하고 세션 상태를 파일에 저장
        /// </summary>
        private static async Task CreateNewSessionAsync()
        {
            try
            {
                // 브라우저가 없으면 종료
                if (_browser == null) return;

                // 새로운 임시 컨텍스트 생성 (세션 상태 없이)
                var ctx = await _browser.NewContextAsync(DefaultContextOptions());
                var page = await ctx.NewPageAsync();
                try
                {
                    // 로그인 페이지로 이동
                    await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });

                    // 자동 로그인 수행
                    await page.FillAsync("#id", UserId);           // 사용자 ID 입력
                    await page.FillAsync("#password", Password);   // 비밀번호 입력
                    await page.ClickAsync("#loginBtn");            // 로그인 버튼 클릭

                    // 로그인 완료 대기
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                    // 현재 세션 상태를 JSON으로 추출
                    var storageState = await ctx.StorageStateAsync();

                    // 세션 상태가 유효하면 파일에 저장
                    if (!string.IsNullOrWhiteSpace(storageState))
                    {
                        await File.WriteAllTextAsync(SessionFilePath, storageState);
                    }
                }
                finally
                {
                    // 임시 컨텍스트 정리
                    await ctx.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                // 새 세션 생성 실패 시 로깅
                Logs.Error($"새 세션 생성 실패: {ex}");
            }
        }

        /// <summary>
        /// 에러 결과 JSON 객체 생성
        /// 표준화된 에러 응답 형식을 제공
        /// </summary>
        /// <param name="message">에러 메시지</param>
        /// <returns>에러 정보가 담긴 JSON 객체</returns>
        private static JObject CreateErrorResult(string message) => new()
        {
            ["success"] = false,           // 실패 상태
            ["message"] = message,         // 에러 메시지
            ["minutesUntilPay"] = 0       // 무료 주차 시간 0분
        };

        /// <summary>
        /// 리소스 정리 메서드
        /// 애플리케이션 종료 시 브라우저 및 Playwright 리소스 해제
        /// </summary>
        public static async Task DisposeResourcesAsync()
        {
            // 브라우저 종료 시도 (예외 무시)
            try { await _browser?.CloseAsync(); } catch { }

            // Playwright 인스턴스 해제
            _playwright?.Dispose();
        }

        /// <summary>
        /// 추가 할인 쿠폰 적용 메서드
        /// 4시간, 1시간, 30분 쿠폰을 순차적으로 최대한 적용
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="getFeeAsync">현재 요금 조회 함수</param>
        /// <param name="onDiscountApplied">할인 적용 시 호출될 콜백 함수</param>
        private async Task ApplyAdditionalCoupons(IPage page, Func<Task<int>> getFeeAsync, Action<int> onDiscountApplied)
        {
            try
            {
                // 쿠폰 정의: 할인액과 해당 버튼 선택자
                var couponDefs = new[]
                {
                    new { price = 16000, selectors = new[] { "button:has-text(\"4시간\")" } },    // 4시간 쿠폰 (16,000원)
                    new { price = 4000, selectors = new[] { "button:has-text(\"1시간\")" } },     // 1시간 쿠폰 (4,000원)
                    new { price = 2000, selectors = new[] { "button:has-text(\"30분\")" } },     // 30분 쿠폰 (2,000원)
                };

                // 30분당 기본 요금
                const int feePer30 = 2000;

                // 각 쿠폰 종류별로 처리 (큰 할인부터)
                foreach (var def in couponDefs)
                {
                    // 해당 쿠폰을 최대한 적용
                    while (true)
                    {
                        try
                        {
                            // 현재 남은 요금 확인
                            int feeBefore = await getFeeAsync();
                            if (feeBefore <= 0) break;  // 요금이 0이하면 더 이상 할인 불필요

                            // 남은 요금에 대한 30분 슬롯 수 계산
                            int remainingSlots = (int)Math.Ceiling(feeBefore / (double)feePer30);

                            // 현재 쿠폰이 커버하는 30분 슬롯 수
                            int couponSlots = def.price / feePer30;

                            // 쿠폰이 남은 요금보다 크면 적용하지 않음 (오버 할인 방지)
                            if (couponSlots > remainingSlots) break;

                            // 쿠폰 버튼 찾기
                            ILocator? btn = null;
                            foreach (var selector in def.selectors)
                            {
                                var candidate = page.Locator(selector).First;
                                if (await candidate.CountAsync() > 0)
                                {
                                    btn = candidate;
                                    break;
                                }
                            }

                            // 버튼이 없으면 해당 쿠폰 적용 중단
                            if (btn == null) break;

                            // 버튼 클릭 실패 시 해당 쿠폰 적용 중단
                            if (!await SafeClickButton(page, btn)) break;

                            // 클릭 후 짧은 대기
                            await Task.Delay(120);

                            // 요금 변경 확인 (최대 6번 시도, 각각 120ms 간격)
                            bool changed = false;
                            for (int t = 0; t < 6; t++)
                            {
                                await Task.Delay(120);
                                int feeAfter = await getFeeAsync();

                                // 요금이 변경되었으면 실제 할인액 계산하여 기록
                                if (feeAfter != feeBefore)
                                {
                                    onDiscountApplied(feeBefore - feeAfter);
                                    changed = true;
                                    break;
                                }
                            }

                            // 요금 변경이 감지되지 않았으면 쿠폰 적용 중단
                            if (!changed) break;
                        }
                        catch (Exception ex)
                        {
                            // 쿠폰 적용 중 오류 발생 시 해당 쿠폰 중단
                            Logs.Info($"할인 쿠폰 적용 오류: {ex.Message}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 전체 쿠폰 처리 실패 시 로깅
                Logs.Error($"추가 쿠폰 처리 전체 오류: {ex}");
            }
        }

        /// <summary>
        /// 할인 정보 테이블 처리 메서드
        /// 페이지의 할인 테이블에서 기존 적용된 할인 정보를 읽어서 처리
        /// </summary>
        /// <param name="page">작업할 페이지 객체</param>
        /// <param name="onDiscountApplied">할인 적용 시 호출될 콜백 함수</param>
        private async Task ProcessDiscountTable(IPage page, Action<int> onDiscountApplied)
        {
            try
            {
                // 테이블의 모든 할인 정보 행 가져오기
                var discountRows = await page.Locator("table tbody tr").AllAsync();

                // 각 행을 순회하며 할인 정보 확인
                foreach (var row in discountRows)
                {
                    try
                    {
                        // 첫 번째 셀에서 할인 종류 텍스트 가져오기
                        var discountTypeCell = await row.Locator("td").First.InnerTextAsync();

                        // 셀 내용이 없으면 다음 행으로
                        if (string.IsNullOrWhiteSpace(discountTypeCell)) continue;

                        // 방문자주차권 할인 발견 시 120분 할인으로 기록
                        if (discountTypeCell.Contains("방문자주차권"))
                        {
                            onDiscountApplied(120);
                        }

                        // 4시간 바코드 할인 발견 시 240분 할인으로 기록
                        if (discountTypeCell.Contains("4시간(바코드할인)"))
                        {
                            onDiscountApplied(240);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 개별 행 처리 실패 시 로깅하고 다음 행 계속 처리
                        Logs.Info($"할인정보 행 처리 오류: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // 전체 테이블 처리 실패 시 로깅
                Logs.Info($"할인정보 테이블 처리 오류: {ex.Message}");
            }
        }
    }
}