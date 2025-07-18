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
                var query = _context.Members
                    .Include(m => m.Cars)
                    .Include(m => m.HelpRequests)
                    .ThenInclude(req => req.HelpDetails)
                    .ThenInclude(detail => detail.HelperMember)
                    .OrderBy(r => r.Id)
                    .AsQueryable();

                // 조건이 있는 것만 차례대로 붙임
                query = string.IsNullOrWhiteSpace(param.memberLoginId)
                    ? query
                    : query.Where(m => m.MemberLoginId.Contains(param.memberLoginId));

                query = string.IsNullOrWhiteSpace(param.memberName)
                    ? query
                    : query.Where(m => m.MemberName.Contains(param.memberName));

                query = string.IsNullOrWhiteSpace(param.carNumber)
                   ? query
                   : query.Where(m => m.Cars.Any(mc => mc.CarNumber.Contains(carNumber)));

                var result = await query.OrderBy(r => r.Id).ToListAsync();
                List<MemberDto> memberDtos = result.Select(m => new MemberDto
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
                    RequestHelpHistory = m.HelpRequests?.Select(req => new ReqHelpDto
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
                            ReqHelpCar = new ReqHelpCarDto
                            {
                                Id = req.HelpReqMember.Cars.First().Id,
                                CarNumber = req.HelpReqMember.Cars.First().CarNumber
                            }
                            
                        },
                        HelpDetails = req.HelpDetails.Select(detail => new ReqHelpDetailDto
                        {
                            Id = detail.Id,
                            ReqDetailStatus = detail.ReqDetailStatus,
                            Helper = detail.HelperMember == null ? null : new HelpMemberDto
                            {
                                Id = detail.HelperMemberId ?? 0,
                                HelperName = detail.HelperMember?.MemberName ?? string.Empty,
                                HelperEmail = detail.HelperMember?.Email ?? string.Empty,
                                SlackId = detail.HelperMember?.SlackId ?? string.Empty
                            },
                            InsertDate = detail.InsertDate
                        }).ToList()
                    }).ToList()
                }).ToList(); 

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

            if (!string.IsNullOrWhiteSpace(query.carNumber))
            {
                var car = member.Cars.FirstOrDefault();
                if (car != null)
                {
                    car.CarNumber = query.carNumber;
                    car.UpdateDate = DateTimeOffset.UtcNow;
                    _context.MemberCars.Update(car);
                }
                else
                {
                    var newCar = new MemberCarModel
                    {
                        MemberId = member.Id,
                        CarNumber = query.carNumber,
                        CreateDate = DateTimeOffset.UtcNow,
                        UpdateDate = DateTimeOffset.UtcNow
                    };
                    await _context.MemberCars.AddAsync(newCar);
                }
            }

            _context.Members.Update(member);
            await _context.SaveChangesAsync();

            returnJob = new JObject
            {
                { "Result", "Success" },
                { "MemberId", member.Id }
            };
            return Ok(returnJob.ToString());
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
