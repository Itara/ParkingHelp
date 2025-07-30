using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;
using System.Diagnostics;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SlackNotifier _slackNotifier;
        public MemberController(AppDbContext context, SlackOptions slackOptions)
        {
            _context = context;
            _slackNotifier = new SlackNotifier(slackOptions);
        }

        [HttpGet("Members")]
        public async Task<IActionResult> GetMembers([FromQuery] MemberGetParam param)
        {
            string memberId = param.memberLoginId ?? string.Empty;
            string memberName = param.memberName ?? string.Empty;
            string carNumber = param.carNumber ?? string.Empty;

            try
            {

                var helpOffers = await _context.HelpOffers
                .Where(o => o.HelpDetails.Any(d => d.RequestMemberId != null))
                .Include(o => o.HelperMember)
                .Include(o => o.HelpDetails)
                    .ThenInclude(d => d.RequestMember)
                        .ThenInclude(m => m.Cars)
                .OrderBy(o => o.Id).ToListAsync();

                // 이후 메모리에서 DTO 조립
                var result = helpOffers
                    .SelectMany(offer =>
                        offer.HelpDetails
                            .Where(d => d.RequestMemberId != null)
                            .Select(d => new
                            {
                                RequestMemberId = d.RequestMemberId!.Value, //위에서 null이 아닌 것만 필터링했으므로 안전하게 사용 가능
                                Offer = new MyHelpOfferDTO
                                {
                                    Id = offer.Id,
                                    Status = offer.Status,
                                    DiscountTotalCount = offer.DiscountTotalCount,
                                    DiscountApplyCount = offer.DiscountApplyCount,
                                    HelperServiceDate = offer.HelerServiceDate,
                                    SlackThreadTs = offer.SlackThreadTs,
                                    Helper = new HelpMemberDto
                                    {
                                        Id = offer.HelperMember.Id,
                                        Name = offer.HelperMember.MemberName,
                                        Email = offer.HelperMember.Email,
                                        SlackId = offer.HelperMember.SlackId
                                    },
                                    HelpOfferDetail = new List<HelpOfferDetailDTO>
                                    {
                            new HelpOfferDetailDTO
                            {
                                Id = d.Id,
                                RequestDate = d.RequestDate,
                                DiscountApplyDate = d.DiscountApplyDate,
                                DiscountApplyType = d.DiscountApplyType,
                                ReqDetailStatus = d.ReqDetailStatus,
                                HelpRequester = new HelpRequesterDto
                                {
                                    Id = d.RequestMember.Id,
                                    HelpRequesterName = d.RequestMember.MemberName,
                                    RequesterEmail = d.RequestMember.Email,
                                    SlackId = d.RequestMember.SlackId,
                                    ReqHelpCar = d?.RequestMember?.Cars?
                                    .Select(c => new ReqHelpCarDto
                                    {
                                        Id = c.Id,
                                        CarNumber = c.CarNumber
                                    })
                                    .FirstOrDefault()
                                }
                            }
                                    }
                                }
                            }))
                    .ToList();

                var groupedByMember = result
                .GroupBy(x => x.RequestMemberId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .GroupBy(x => x.Offer)
                        .Select(og =>
                        {
                            var baseInfo = og.First();
                            return new MyHelpOfferDTO
                            {
                                Id = baseInfo.Offer.Id,
                                DiscountTotalCount = baseInfo.Offer.DiscountTotalCount,
                                DiscountApplyCount = baseInfo.Offer.DiscountApplyCount,
                                HelperServiceDate = baseInfo.Offer.HelperServiceDate,
                                Helper = baseInfo.Offer.Helper,
                                HelpOfferDetail = baseInfo.Offer.HelpOfferDetail
                            };
                        })
                        .ToList()
                );


                var memberDtos = await _context.Members.Where(m =>
                    (string.IsNullOrWhiteSpace(param.memberLoginId) || m.MemberLoginId.Contains(param.memberLoginId)) &&
                    (string.IsNullOrWhiteSpace(param.memberName) || m.MemberName.Contains(param.memberName)) &&
                    (string.IsNullOrWhiteSpace(param.carNumber) || m.Cars.Any(c => c.CarNumber.Contains(param.carNumber)))
                )
                .OrderBy(m => m.Id)
                .Select(m => new MemberDto
                {
                    Id = m.Id,
                    Name = m.MemberName,
                    Email = m.Email ?? "",
                    MemberLoginId = m.MemberLoginId,
                    SlackId = m.SlackId,

                    Cars = m.Cars.Select(car => new MemberCarDTO
                    {
                        Id = car.Id,
                        CarNumber = car.CarNumber
                    }).ToList(),

                    RequestHelpHistory = m.HelpRequests.Select(req => new ReqHelpDto
                    {
                        Id = req.Id,
                        ApplyDisCount = req.DiscountApplyCount,
                        TotalDisCount = req.DiscountTotalCount,
                        Status = req.Status,
                        ReqDate = req.ReqDate,
                        HelpRequester = new HelpRequesterDto
                        {
                            Id = req.HelpReqMember.Id,
                            HelpRequesterName = req.HelpReqMember.MemberName,
                            RequesterEmail = req.HelpReqMember.Email,
                            SlackId = req.HelpReqMember.SlackId,
                            ReqHelpCar = req.HelpReqMember.Cars.Select(c => new ReqHelpCarDto
                            {
                                Id = c.Id,
                                CarNumber = c.CarNumber
                            }).FirstOrDefault()
                        },
                        HelpDetails = req.HelpDetails.Select(detail => new ReqHelpDetailDto
                        {
                            Id = detail.Id,
                            ReqDetailStatus = detail.ReqDetailStatus,
                            DiscountApplyDate = detail.DiscountApplyDate,
                            DiscountApplyType = detail.DiscountApplyType,
                            InsertDate = detail.InsertDate,
                            Helper = detail.HelperMember == null ? null : new HelpMemberDto
                            {
                                Id = detail.HelperMemberId ?? 0,
                                Name = detail.HelperMember.MemberName,
                                Email = detail.HelperMember.Email,
                                SlackId = detail.HelperMember.SlackId
                            }
                        }).ToList()
                    }).ToList(),

                    HelpOfferHistory = m.HelpOffers.Select(offer => new HelpOfferDTO
                    {
                        Id = offer.Id,
                        Status = offer.Status,
                        DiscountTotalCount = offer.DiscountTotalCount,
                        DiscountApplyCount = offer.DiscountApplyCount,
                        HelpOfferType = offer.HelpOfferType,
                        HelperServiceDate = offer.HelerServiceDate,
                        Helper = new HelpMemberDto
                        {
                            Id = offer.HelperMember.Id,
                            Name = offer.HelperMember.MemberName,
                            Email = offer.HelperMember.Email,
                            SlackId = offer.HelperMember.SlackId
                        },
                        HelpOfferDetail = offer.HelpDetails.Select(detail => new HelpOfferDetailDTO
                        {
                            Id = detail.Id,
                            RequestDate = detail.RequestDate,
                            DiscountApplyDate = detail.DiscountApplyDate,
                            DiscountApplyType = detail.DiscountApplyType,
                            ReqDetailStatus = detail.ReqDetailStatus,
                            HelpRequester = detail.RequestMember == null ? null : new HelpRequesterDto
                            {
                                Id = detail.RequestMember.Id,
                                HelpRequesterName = detail.RequestMember.MemberName,
                                RequesterEmail = detail.RequestMember.Email,
                                SlackId = detail.RequestMember.SlackId,
                                ReqHelpCar = detail.RequestMember.Cars.Select(c => new ReqHelpCarDto
                                {
                                    Id = c.Id,
                                    CarNumber = c.CarNumber
                                }).FirstOrDefault()
                            }
                        }).ToList()
                    }).ToList(),
                    HelpOfferMyRequestHistory = groupedByMember.ContainsKey(m.Id) ? groupedByMember[m.Id] : new List<MyHelpOfferDTO>() 
                }).OrderBy(o => o.Id).ToListAsync();

                var kstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
                var nowKST = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kstTimeZone);
                var startOfTodayKST = new DateTimeOffset(nowKST.Date, kstTimeZone.GetUtcOffset(nowKST.Date));
                var endOfTodayKST = startOfTodayKST.AddDays(1).AddSeconds(-1);

                // UTC 변환
                var startOfTodayUTC = startOfTodayKST.UtcDateTime;
                var endOfTodayUTC = endOfTodayKST.UtcDateTime;


                foreach (var memberDto in memberDtos)
                {
                    //myHelpRequest
                    var myHelpRequests = await _context.ReqHelps
                        .Include(r => r.HelpReqMember)
                        .ThenInclude(m => m.Cars)
                        .Include(r => r.HelpDetails)
                        .Where(r => r.HelpDetails.Any(d => d.HelperMemberId == memberDto.Id && d.ReqDetailStatus == ReqDetailStatus.Completed && d.InsertDate >= startOfTodayUTC && d.InsertDate <= endOfTodayUTC)).ToListAsync();
                    List<ReqHelpDto> tmpHistory = myHelpRequests.Select(req => new ReqHelpDto
                    {
                        Id = req.Id,
                        ApplyDisCount = req.DiscountApplyCount,
                        TotalDisCount = req.DiscountTotalCount,
                        Status = req.Status,
                        ReqDate = req.ReqDate,
                        HelpRequester = new HelpRequesterDto
                        {
                            Id = req.HelpReqMember.Id,
                            HelpRequesterName = req.HelpReqMember.MemberName,
                            RequesterEmail = req.HelpReqMember.Email,
                            SlackId = req.HelpReqMember.SlackId,
                            ReqHelpCar = req.HelpReqMember.Cars.Select(c => new ReqHelpCarDto
                            {
                                Id = c.Id,
                                CarNumber = c.CarNumber
                            }).FirstOrDefault()
                        },
                        HelpDetails = req.HelpDetails.Where(x => x.ReqDetailStatus == ReqDetailStatus.Completed && x.HelperMemberId == memberDto.Id).Select(detail => new ReqHelpDetailDto
                        {
                            Id = detail.Id,
                            ReqDetailStatus = detail.ReqDetailStatus,
                            DiscountApplyDate = detail.DiscountApplyDate,
                            DiscountApplyType = detail.DiscountApplyType,
                            InsertDate = detail.InsertDate,
                            Helper = detail.HelperMember == null ? null : new HelpMemberDto
                            {
                                Id = detail.HelperMemberId ?? 0,
                                Name = detail.HelperMember.MemberName,
                                Email = detail.HelperMember.Email,
                                SlackId = detail.HelperMember.SlackId
                            }
                        }).ToList()
                    }).ToList();

                    memberDto.MyRequestHelpCompleteHistory = tmpHistory;
                }

                return Ok(memberDtos);
            }
            catch (Exception ex)
            {
                JObject returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrorMsg", $"{ex.Message}" }
                };
                return BadRequest(returnJob.ToString());
            }
        }
        // POST: api/Member
        [HttpPost()]
        public async Task<IActionResult> AddNewMember([FromBody] MemberAddParam query)
        {
            try
            {
                var newMember = new MemberModel
                {
                    MemberLoginId = query.memberLoginId,
                    MemberName = query.memberName,
                    Email = query.email ?? "",
                };
                _context.Members.Add(newMember);
                await _context.SaveChangesAsync();
                var newCar = new MemberCarModel
                {
                    CarNumber = query.carNumber,
                    MemberId = newMember.Id,
                    CreateDate = DateTimeOffset.UtcNow,
                    UpdateDate = DateTimeOffset.UtcNow
                };
                await _context.MemberCars.AddAsync(newCar);
                await _context.SaveChangesAsync();
                //Email로 슬랙 계정 검색
                if (!string.IsNullOrEmpty(newMember.Email))
                {
                    SlackUserByEmail? findUser = await _slackNotifier.FindUserByEmailAsync(newMember.Email);
                    if (findUser != null)
                    {
                        newMember.SlackId = findUser?.Id;
                        _context.Members.Update(newMember);
                        await _context.SaveChangesAsync();
                    }
                }

                MemberDto newMemberDto = new MemberDto
                {
                    Id = newMember.Id,
                    Name = newMember.MemberName,
                    Email = newMember.Email ?? "",
                    MemberLoginId = newMember.MemberLoginId,
                    SlackId = newMember.SlackId,
                    Cars = newMember.Cars.Select(car => new MemberCarDTO
                    {
                        Id = car.Id,
                        CarNumber = car.CarNumber
                    }).ToList()
                };

                return Ok(newMemberDto);
            }
            catch (Exception ex)
            {
                JObject returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrorMsg",$"{ex.InnerException}"}
                 };
                return BadRequest(returnJob.ToString());
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMember(int id, [FromBody] MemberUpdateParam query)
        {
            JObject returnJob = new JObject();
            var member = await _context.Members.Include(m => m.Cars).FirstOrDefaultAsync(m => m.Id == id);
            if (member == null)
            {
                returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrMsg", "사용자가 존재하지 않습니다" }
                };
                return BadRequest(returnJob.ToString());
            }

            try
            {
                member.Email = query.Email ?? member.Email;
                member.MemberName = query.MemberName ?? member.MemberName;
                _context.Members.Update(member);

                if (!string.IsNullOrWhiteSpace(query.carNumber))
                {
                    var car = member.Cars.FirstOrDefault();
                    if (car != null)
                    {
                        car.CarNumber = query.carNumber;
                        car.UpdateDate = DateTimeOffset.UtcNow;
                        _context.MemberCars.Update(car);
                    }
                }

                await _context.SaveChangesAsync();

                var memberDTO = new MemberDto
                {
                    Id = member.Id,
                    MemberLoginId = member.MemberLoginId,
                    Email = member.Email,
                    Cars = member.Cars.Select(x => new MemberCarDTO
                    {
                        Id = x.Id,
                        CarNumber = x.CarNumber,
                        MemberId = x.MemberId
                    }).ToList()
                };

                return Ok(memberDTO);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMember(int id)
        {
            JObject returnJob = new JObject();

            if (_context.Members.Any(m => m.Id == id))
            {
                _context.Members.RemoveRange(_context.Members.Where(m => m.Id == id));
                await _context.SaveChangesAsync();

                returnJob = new JObject
                {
                    { "Result", "Success" },
                    { "MemberId", id }
                };
                return Ok(returnJob.ToString());
            }
            else
            {
                returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrMsg", "사용자가 존재하지 않습니다" }
                };
                return BadRequest(returnJob.ToString());
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
