using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkingHelp.DB;
using ParkingHelp.DB.DTO;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using System.Linq;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RankingController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RankingController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetHelpRanking([FromQuery] RankingGetParam param)
        {

            var today = DateTime.UtcNow.Date;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = new DateTime(today.Year, today.Month, 1)
                .AddMonths(1)
                .AddSeconds(-1);
            try
            {
                DateTime fromDate = param.FromDate ?? startOfMonth;
                DateTime toDate = param.ToDate ?? endOfMonth;
                //1.도와주세요 통계
                var reqHelpStats = _context.ReqHelps
               .Where(r => r.Status == CarHelpStatus.Completed && r.Helper != null)
               .Select(r => new { HelperId = r.Helper!.Id })
               .GroupBy(r => r.HelperId)
               .Select(g => new
               {
                   MemberId = g.Key,
                   Count = g.Count()
               });
                //2.도와줄수있어요 통계
                var helpOfferStats = _context.HelpOffers
                .Where(h => h.Status == CarHelpStatus.Completed && h.Requester != null)
                .GroupBy(h => h.HelperMemId)
                .Select(g => new
                {
                    MemberId = g.Key,
                    Count = g.Count()
                });
                // 1 + 2 합계
                var combinedStats = reqHelpStats.Concat(helpOfferStats).GroupBy(x => x.MemberId)
                    .Select(g => new
                    {
                       MemberId = g.Key,
                       TotalHelpCount = g.Sum(x => x.Count)
                    });

                // 정렬 + Join + Take
                var query = combinedStats
                    .Join
                    (_context.Members, stat => stat.MemberId, mem => mem.Id,
                        (stat, mem) => new RankingDTO
                        {
                            Id = mem.Id,
                            MemberName = mem.MemberName,
                            TotalHelpCount = stat.TotalHelpCount
                        }
                    );

                switch (param.OrderType)
                {
                    case RankingOrderType.Ascending:
                        query = query.OrderBy(x => x.TotalHelpCount);
                        break;
                    case RankingOrderType.Descending:
                        query = query.OrderByDescending(x => x.TotalHelpCount);
                        break;
                }

                var result = await query.Take(param.MaxCount).ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Result = "Fail", ErrMsg = ex.Message });
            }
        }
    }
}
