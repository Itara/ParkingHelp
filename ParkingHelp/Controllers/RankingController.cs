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
                var helpOfferData = await _context.HelpOffersDetail
                .Where(d => d.ReqDetailStatus == ReqDetailStatus.Completed
                            && d.HelpOffer.HelperMember != null
                            && d.RequestMember != null
                            && d.DiscountApplyDate != null)
                .Select(d => new
                {
                    HelperId = d.HelpOffer.HelperMember.Id,
                    HelperName = d.HelpOffer.HelperMember.MemberName,
                    RequestMemberId = d.RequestMember.Id,
                    DiscountApplyType = d.DiscountApplyType,
                    DiscountApplyDate = d.DiscountApplyDate.Value,
                    ReqId = d.Id
                })
                .ToListAsync();

                // 그룹핑 및 RankingDTO 변환
                var rankingList = helpOfferData
                    .GroupBy(d => new { d.HelperId, d.HelperName })
                    .Select((g, index) => new RankingDTO
                    {
                        Id = g.Key.HelperId,
                        MemberName = g.Key.HelperName,
                        TotalHelpCount = g.Count(),
                        Ranking = 0, // 일단 0으로 설정, 아래에서 순위 정렬
                        HelpOfferHistoryDto = new HelperHistoryDto
                        {
                            HelperId = g.Key.HelperId,
                            HelperName = g.Key.HelperName,
                            HelpCount = g.Count(),
                            HelpHistorys = g.Select(x => new HelpHistoryDto
                            {
                                ReqId = x.ReqId,
                                HelpDate = x.DiscountApplyDate,
                                RequestMemberId = x.RequestMemberId,
                                DiscountApplyType = x.DiscountApplyType
                            }).ToList()
                        }
                    })
                    .OrderByDescending(r => r.TotalHelpCount)
                    .ToList();


                var reqHelpData = await _context.ReqHelpsDetail
                .Where(d => d.ReqDetailStatus == ReqDetailStatus.Completed
                            && d.HelperMember != null
                            && d.DiscountApplyDate != null
                            && d.ReqHelps.HelpReqMember != null)
                .Select(d => new
                {
                    HelperId = d.HelperMember.Id,
                    HelperName = d.HelperMember.MemberName,
                    RequestMemberId = d.ReqHelps.HelpReqMember.Id,
                    DiscountApplyType = d.DiscountApplyType,
                    DiscountApplyDate = d.DiscountApplyDate.Value,
                    ReqId = d.Id
                })
                .ToListAsync();

                var reqGrouped = reqHelpData
                    .GroupBy(d => new { d.HelperId, d.HelperName })
                    .Select(g => new
                    {
                        HelperId = g.Key.HelperId,
                        HelperName = g.Key.HelperName,
                        HelpCount = g.Count(),
                        HelpHistorys = g.Select(x => new HelpHistoryDto
                        {
                            ReqId = x.ReqId,
                            HelpDate = x.DiscountApplyDate,
                            RequestMemberId = x.RequestMemberId,
                            DiscountApplyType = x.DiscountApplyType
                        }).ToList()
                    })
                    .ToList();


                foreach (var match in reqGrouped)
                {
                    var existing = rankingList.FirstOrDefault(r => r.Id == match.HelperId);
                    if (existing != null)
                    {
                        // 이미 존재하면 Req 도우미 이력만 추가
                        existing.ReqestHelpHistoryDto = new HelperHistoryDto
                        {
                            HelperId = match.HelperId,
                            HelperName = match.HelperName,
                            HelpCount = match.HelpCount,
                            HelpHistorys = match.HelpHistorys
                        };
                        existing.TotalHelpCount += match.HelpCount;
                    }
                    else
                    {
                        // 존재하지 않으면 새로 추가
                        rankingList.Add(new RankingDTO
                        {
                            Id = match.HelperId,
                            MemberName = match.HelperName,
                            TotalHelpCount = match.HelpCount,
                            Ranking = 0, // 나중에 정렬하면서 재계산
                            ReqestHelpHistoryDto = new HelperHistoryDto
                            {
                                HelperId = match.HelperId,
                                HelperName = match.HelperName,
                                HelpCount = match.HelpCount,
                                HelpHistorys = match.HelpHistorys
                            },
                            HelpOfferHistoryDto = null
                        });
                    }
                }

                var sorted = rankingList.OrderByDescending(r => r.TotalHelpCount).ToList();

                int currentRank = 1;
                int sameCount = 1;
                int prevHelpCount = -1;

                for (int i = 0; i < sorted.Count; i++)
                {
                    var r = sorted[i];
                    if (r.TotalHelpCount == prevHelpCount)
                    {
                        r.Ranking = currentRank;
                        sameCount++;
                    }
                    else
                    {
                        currentRank = i + 1;
                        r.Ranking = currentRank;
                        sameCount = 1;
                        prevHelpCount = r.TotalHelpCount;
                    }
                }
                List<RankingDTO> finalRankingList = sorted;

                return Ok(finalRankingList);
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
