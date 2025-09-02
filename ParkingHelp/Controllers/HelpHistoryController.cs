using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelpHistoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public HelpHistoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet()]
        public async Task<IActionResult> GetHelpHistory([FromQuery] HelpHistoryGetParam param)
        {
            try
            {
                DateTimeOffset startOfToday = DateTimeOffset.UtcNow.Date; // 현재 날짜의 시작
                DateTimeOffset endOfToday = startOfToday.AddDays(1).AddSeconds(-1); // 현재 날짜의 끝
                DateTimeOffset from = param.FromHelpDate ?? startOfToday;
                DateTimeOffset to = param.ToHelpDate ?? endOfToday;

                var histories = await _context.HelpHistories
                .Include(h => h.HelperMember)
                .Include(h => h.ReceiveMember)
                .Select(h => new HelpHistoryDTO
                {
                    Id = h.Id,
                    HelpType = h.HelpType,
                    HelperMemberId = h.HelpMemberId,
                    HelperMemberName = h.HelperMember != null ? h.HelperMember.MemberName : null,
                    ReceiveHelpMemberId = h.ReceiveHelpMemberId,
                    ReceiveMemberName = h.ReceiveMember != null ? h.ReceiveMember.MemberName : null,
                    DiscountApplyType = h.DiscountApplyType,
                    DetailId = h.DetailId,
                    HelpCompleteDate = h.HelpCompleteDate
                })
                .ToListAsync();
                return Ok(histories);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Result = "Fail", ErrMsg = ex.Message });
            }
        }

      
    }
}
