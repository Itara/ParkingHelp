using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DTO;
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

            var today = DateTimeOffset.Now;
            var startOfMonth = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, today.Offset);
            var endOfMonth = startOfMonth.AddMonths(1);  // 다음 달 1일

            try
            {
                DateTimeOffset fromDate = param.FromDate ?? startOfMonth;
                DateTimeOffset toDate = param.ToDate ?? endOfMonth;
                //1.도와주세요 통계
                var helperStats = await _context.ReqHelpsDetail
               .Where(r => r.HelperMemberId != null)
               .GroupBy(r => new { r.HelperMemberId, r.HelperMember!.MemberName })
               .Select(g => new
               {
                   HelperId = g.Key.HelperMemberId,
                   HelperName = g.Key.MemberName,
                   HelpCount = g.Count(),
                   FirstHelpDate = g.Min(x => x.InsertDate),
                   LastHelpDate = g.Max(x => x.InsertDate),
                   RecentHelps = g.OrderByDescending(x => x.InsertDate).Take(3).ToList()
               })
               .OrderByDescending(x => x.HelpCount)
               .ToListAsync();
                //2.도와줄수있어요 통계
                var helpOfferStats = _context.HelpOffers
                //.Where(h => h.Status == CarHelpStatus.Completed && h.Requester != null)
                .GroupBy(h => h.HelperMemId)
                .Select(g => new
                {
                    MemberId = g.Key,
                    Count = g.Count()
                });
                // 1 + 2 합계
                var combinedStats = helpOfferStats.Concat(helpOfferStats).GroupBy(x => x.MemberId)
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

                var result = await query.Take(param.MaxCount ?? 5).ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }
        private JObject GetErrorJobject(string errorMessage, string InnerExceptionMessage)
        {
            return new JObject
            {
                { "Result", "Error" },
                { "ErrorMsg", errorMessage },
                { "InnerException" , InnerExceptionMessage}
            };
        }

    }
}
