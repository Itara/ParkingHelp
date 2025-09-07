using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Services.ParkingDiscount;
using ParkingHelp.SlackBot;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AutomationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SlackNotifier _slackNotifier;
        private readonly ParkingAutomation _parkingAutomation;

        public AutomationController(AppDbContext context, SlackOptions slackOptions, ParkingAutomation parkingAutomation)
        {
            _context = context;
            _slackNotifier = new SlackNotifier(slackOptions);
            _parkingAutomation = parkingAutomation;
        }

        /// <summary>
        /// 주차장 퇴근 시스템 API
        /// </summary>
        [HttpGet()]
        public async Task<IActionResult> Automation([FromQuery] MemberGetParam param)
        {
            try
            {
                JObject result = await _parkingAutomation.ProcessParkingForApiAsync(param.carNumber);

                if (result != null)
                {
                    if ((bool)result["success"])
                    {
                        return Ok(result.ToString());
                    }
                    else
                    {
                        // 실패 시 슬랙 알림
                        await _slackNotifier.SendMessageAsync($"차량번호: {param.carNumber} 처리 실패 - {result["message"]}", null);
                        return BadRequest(result.ToString());
                    }
                }
                else
                {
                    var errorResult = new JObject
                    {
                        ["success"] = false,
                        ["message"] = "할인권 요청중 오류가 발생했습니다.",
                        ["minutesUntilPay"] = 0
                    };
                    await _slackNotifier.SendMessageAsync($"차량번호: {param.carNumber} 처리 중 오류 발생", null);
                    return BadRequest(errorResult.ToString());
                }
            }
            catch (Exception ex)
            {
                var errorResult = new JObject
                {
                    ["success"] = false,
                    ["message"] = $"서버 오류: {ex.Message}",
                    ["minutesUntilPay"] = 0
                };
                await _slackNotifier.SendMessageAsync($"차량번호: {param.carNumber} 서버 오류 - {ex.Message}", null);
                return BadRequest(errorResult.ToString());
            }
        }
    }
}