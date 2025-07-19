using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelpOfferController : ControllerBase
    {
        private readonly AppDbContext _context;

        public HelpOfferController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 도와줄수있는 리스트 
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet()]
        public async Task<IActionResult> GetHelpOfferList([FromQuery] HelpOfferParam query)
        {
            try
            {
                var kstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
                var nowKST = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kstTimeZone);
                var startOfTodayKST = new DateTimeOffset(nowKST.Date, kstTimeZone.GetUtcOffset(nowKST.Date));
                var endOfTodayKST = startOfTodayKST.AddDays(1).AddSeconds(-1);

                DateTimeOffset fromDate = query.FromReqDate ?? startOfTodayKST;
                DateTimeOffset toDate = query.ToReqDate ?? endOfTodayKST;

                var helpOfferList = await _context.HelpOffers
                    .Include(h => h.HelperMember)
                    .ThenInclude(m => m.Cars)
                    .Include(r => r.HelpDetails)
                    .Where(x => x.HelerServiceDate >= fromDate.UtcDateTime && x.HelerServiceDate <= toDate.UtcDateTime)
                    .ToListAsync();

                var reqhelpOfferListDto = helpOfferList.Select(h => new HelpOfferDTO
                {
                    Id = h.Id,
                    Status = h.Status,
                    HelperServiceDate = h.HelerServiceDate,
                    DiscountTotalCount = h.DiscountTotalCount,
                    DiscountApplyCount = h.DiscountApplyCount,
                    SlackThreadTs = h.SlackThreadTs,
                    Helper = new HelpMemberDto
                    {
                        Id = h.HelperMember.Id,
                        Name = h.HelperMember.MemberName,
                        Email = h.HelperMember.Email,
                        SlackId = h.HelperMember.SlackId
                    },
                    HelpOfferDetail = h.HelpDetails.Select(d => new HelpOfferDetailDTO
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        DiscountApplyType = d.DiscountApplyType,
                        RequestDate = d.RequestDate,
                        HelpRequester = d.RequestMember == null ? null : new HelpRequesterDto
                        {
                            Id = d.RequestMember.Id,
                            HelpRequesterName = d.RequestMember.MemberName,
                            RequesterEmail = d.RequestMember.Email,
                            SlackId = d.RequestMember.SlackId,
                            ReqHelpCar = d.RequestMember.Cars.Select(c => new ReqHelpCarDto
                            {
                                Id = c.Id,
                                CarNumber = c.CarNumber
                            }).FirstOrDefault()
                        }
                    }).ToList()
                }).ToList();

                return Ok(reqhelpOfferListDto);
            }
            catch (Exception ex)
            {
                JObject result = new JObject
                {
                    { "Result", "Fail" },
                    { "ErrMsg", ex.Message }
                };
                return BadRequest(result);
            }
        }

        [HttpPost()]
        public async Task<IActionResult> PostRequestHelp([FromBody] RequestHelpPostParam query)
        {
            try
            {
                int helpReqMemId = query.HelpReqMemId ?? 0;
                var member =  await _context.Members.Where(x => x.Id  == helpReqMemId).FirstOrDefaultAsync();

                if (helpReqMemId == 0 || member == null  )
                {
                    JObject jResult = GetErrorJobject("사용자를 찾을수없습니다.","");
                    return BadRequest(jResult.ToString());
                }

                var newHelpOffer = new HelpOfferModel
                {
                    HelperMemId = query.HelpReqMemId!.Value,
                    Status = HelpStatus.Waiting,
                    HelerServiceDate = DateTimeOffset.UtcNow,
                    DiscountTotalCount = query.TotalDisCount,
                    DiscountApplyCount = 0
                };

                _context.HelpOffers.Add(newHelpOffer);
                await _context.SaveChangesAsync();
                for (int i= 0; i < query.TotalDisCount; i++)
                {
                    _context.HelpOffersDetail.Add(new HelpOfferDetailModel
                    {
                        HelpOfferId = newHelpOffer.Id,
                        ReqDetailStatus = ReqDetailStatus.Waiting
                    });
                }
                await _context.SaveChangesAsync();


                // navigation 로딩
                await _context.Entry(newHelpOffer).Reference(h => h.HelperMember).LoadAsync();
                await _context.Entry(newHelpOffer).Collection(h => h.HelpDetails)
                    .Query().Include(d => d.RequestMember).ThenInclude(m => m.Cars).LoadAsync();

                // DTO로 매핑
                var newHelperOfferDto = new HelpOfferDTO
                {
                    Id = newHelpOffer.Id,
                    Status = newHelpOffer.Status,
                    HelperServiceDate = newHelpOffer.HelerServiceDate,
                    DiscountTotalCount = newHelpOffer.DiscountTotalCount,
                    DiscountApplyCount = newHelpOffer.DiscountApplyCount,
                    SlackThreadTs = newHelpOffer.SlackThreadTs,
                    Helper = new HelpMemberDto
                    {
                        Id = newHelpOffer.HelperMember.Id,
                        Name = newHelpOffer.HelperMember.MemberName,
                        Email = newHelpOffer.HelperMember.Email,
                        SlackId = newHelpOffer.HelperMember.SlackId
                    },
                    HelpOfferDetail = newHelpOffer.HelpDetails.Select(d => new HelpOfferDetailDTO
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        DiscountApplyType = d.DiscountApplyType,
                        RequestDate = d.RequestDate,
                        HelpRequester = d.RequestMember == null ? null : new HelpRequesterDto
                        {
                            Id = d.RequestMember.Id,
                            HelpRequesterName = d.RequestMember.MemberName,
                            RequesterEmail = d.RequestMember.Email,
                            SlackId = d.RequestMember.SlackId,
                            ReqHelpCar = d.RequestMember.Cars.Select(c => new ReqHelpCarDto
                            {
                                Id = c.Id,
                                CarNumber = c.CarNumber
                            }).FirstOrDefault()
                        }
                    }).ToList()
                };


                return Ok(newHelperOfferDto);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutHelpOffer(int id, [FromBody] HelpOfferPutParam query)
        {
            if (query == null)
                return BadRequest("요청 데이터가 없습니다.");

            try
            {
                var helpOffer = await _context.HelpOffers
                    .Include(h => h.HelperMember)
                    .ThenInclude(h => h.Cars)
                    .Include(h => h.HelpDetails)
                    .ThenInclude(d => d.RequestMember)
                    .ThenInclude(m => m.Cars)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(h => h.Id == id);

                if (helpOffer == null)
                    return NotFound("도움 요청이 존재하지 않습니다.");

                var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    helpOffer.HelerServiceDate = query.HelpDate ?? helpOffer.HelerServiceDate;
                    helpOffer.Status = query.Status ?? helpOffer.Status;

                    var detailIds = query.HelpOfferDetail?.Select(x => x.Id).ToHashSet() ?? new HashSet<int>();
                    var existingDetails = helpOffer.HelpDetails.Where(d => detailIds.Contains(d.Id)).ToDictionary(d => d.Id);

                    if (query.HelpOfferDetail != null)
                    {
                        foreach (var detail in query.HelpOfferDetail)
                        {
                            if (!existingDetails.TryGetValue(detail.Id, out var existing))
                                continue;

                            var statusChanged = detail.Status.HasValue && existing.ReqDetailStatus != detail.Status.Value;

                            existing.ReqDetailStatus = detail.Status ?? existing.ReqDetailStatus;
                            existing.DiscountApplyType = detail.DiscountApplyType ?? existing.DiscountApplyType;

                            if (statusChanged && existing.ReqDetailStatus == ReqDetailStatus.Completed)
                            {
                                existing.DiscountApplyDate = DateTimeOffset.UtcNow;
                            }
                            else if (detail.DiscountApplyDate.HasValue)
                            {
                                existing.DiscountApplyDate = detail.DiscountApplyDate;
                            }
                        }
                    }

                    var completedCount = helpOffer.HelpDetails.Count(d => d.ReqDetailStatus == ReqDetailStatus.Completed);
                    helpOffer.DiscountApplyCount = completedCount;

                    if (!query.Status.HasValue)
                    {
                        if (helpOffer.DiscountApplyCount == helpOffer.DiscountTotalCount)
                        {
                            helpOffer.Status = HelpStatus.Completed;
                        }
                        else if (helpOffer.DiscountApplyCount > 0)
                        {
                            helpOffer.Status = HelpStatus.Check;
                        }
                        else
                        {
                            helpOffer.Status = HelpStatus.Waiting;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var updatedDto = new HelpOfferDTO
                    {
                        Id = helpOffer.Id,
                        Status = helpOffer.Status,
                        HelperServiceDate = helpOffer.HelerServiceDate,
                        DiscountTotalCount = helpOffer.DiscountTotalCount,
                        DiscountApplyCount = helpOffer.DiscountApplyCount,
                        SlackThreadTs = helpOffer.SlackThreadTs,
                        Helper = new HelpMemberDto
                        {
                            Id = helpOffer.HelperMember.Id,
                            Name = helpOffer.HelperMember.MemberName,
                            Email = helpOffer.HelperMember.Email,
                            SlackId = helpOffer.HelperMember.SlackId
                        },
                        HelpOfferDetail = helpOffer.HelpDetails.Select(d => new HelpOfferDetailDTO
                        {
                            Id = d.Id,
                            ReqDetailStatus = d.ReqDetailStatus,
                            DiscountApplyDate = d.DiscountApplyDate,
                            DiscountApplyType = d.DiscountApplyType,
                            RequestDate = d.RequestDate,
                            HelpRequester = d.RequestMember == null ? null : new HelpRequesterDto
                            {
                                Id = d.RequestMember.Id,
                                HelpRequesterName = d.RequestMember.MemberName,
                                RequesterEmail = d.RequestMember.Email,
                                SlackId = d.RequestMember.SlackId,
                                ReqHelpCar = d.RequestMember.Cars.Select(c => new ReqHelpCarDto
                                {
                                    Id = c.Id,
                                    CarNumber = c.CarNumber
                                }).FirstOrDefault()
                            }
                        }).ToList()
                    };

                    return Ok(updatedDto);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return BadRequest(GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null"));
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRequestHelp(int id)
        {
            var reqHelp = await _context.HelpOffers.FirstOrDefaultAsync(x => x.Id == id);

            if (reqHelp == null)
            {
                return NotFound("요청이 존재하지 않습니다.");
            }

            try
            {
                _context.HelpOffers.Remove(reqHelp);
                await _context.SaveChangesAsync();
                return Ok();
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
