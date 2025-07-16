using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;
using System.Linq;
namespace ParkingHelp.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class RequestHelpController : ControllerBase
    {
        private readonly AppDbContext _context;

        private readonly SlackNotifier _slackNotifier;
        public RequestHelpController(AppDbContext context, SlackOptions slackOptions)
        {
            _context = context;
            _slackNotifier = new SlackNotifier(slackOptions);
        }

        /// <summary>
        /// 주차등록 요청 조회
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet()]
        public async Task<IActionResult> GetRequestHelp([FromQuery] RequestHelpGetParam query)
        {
            try
            {
                TimeZoneInfo kstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

                DateTime nowKST = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kstZone);
                DateTime startOfTodayKST = nowKST.Date; // 오늘 자정 (KST)
                DateTime endOfTodayKST = startOfTodayKST.AddDays(1).AddSeconds(-1); // 오늘 23:59:59 (KST)

                DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startOfTodayKST, kstZone);
                DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(endOfTodayKST, kstZone);

                DateTime fromDate = query.FromReqDate ?? startUtc;
                DateTime toDate = query.ToReqDate ?? endUtc;

                var reqHelpsQuery = _context.ReqHelps
                    .Include(r => r.HelpRequester)
                    .Include(r => r.Helper)
                    .Include(r => r.ReqCar)
                    .Where(x => x.ReqDate >= fromDate && x.ReqDate <= toDate);

                if (query.HelpReqMemId.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.HelpRequester.Id == query.HelpReqMemId);
                }

                if (query.HelperMemId.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.Helper != null && r.Helper.Id == query.HelperMemId);
                }

                if (!string.IsNullOrEmpty(query.ReqCarNumber))
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.ReqCar != null && r.ReqCar.CarNumber == query.ReqCarNumber);
                }

                if (query.Status.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.Status == query.Status);
                }

                var reqHelps = await reqHelpsQuery
                .Select(r => new ReqHelpDto
                {
                    Id = r.Id,
                    ReqDate = r.ReqDate,
                    HelpDate = r.HelpDate,
                    Status = r.Status,
                    HelpRequester = new HelpRequesterDto
                    {
                        Id = r.HelpRequester.Id,
                        HelpRequesterName = r.HelpRequester.MemberName
                    },
                    Helper = r.Helper == null ? null : new HelperDto
                    {
                        Id = r.Helper.Id,
                        HelperName = r.Helper.MemberName
                    },
                    ReqCar = r.ReqCar == null ? null : new ReqHelpCarDto
                    {
                        Id = r.ReqCar.Id,
                        CarNumber = r.ReqCar.CarNumber
                    }
                })
                .OrderBy(r => r.Status)
                .ThenBy(r => r.ReqDate)
                .ToListAsync();

                return Ok(reqHelps);
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
                var newReqHelp = new ReqHelp
                {
                    HelpReqMemId = query.HelpReqMemId ?? 0,
                    Status = 0,
                    ReqCarId = query.CarId,
                    ReqDate = DateTime.UtcNow
                };
                _context.ReqHelps.Add(newReqHelp);
                await _context.SaveChangesAsync();

                var returnNewReqHelps = await _context.ReqHelps
                   .Include(r => r.HelpRequester)
                   .Include(r => r.Helper)
                   .Include(r => r.ReqCar)
                   .Select(r => new ReqHelpDto
                   {
                       Id = r.Id,
                       ReqDate = r.ReqDate,
                       HelpDate = r.HelpDate,
                       Status = r.Status,
                       HelpRequester = new HelpRequesterDto
                       {
                           Id = r.HelpRequester.Id,
                           HelpRequesterName = r.HelpRequester.MemberName,
                           RequesterEmail = r.HelpRequester.Email,
                           SlackId = r.HelpRequester.SlackId
                       },
                       Helper = r.Helper == null ? null : new HelperDto
                       {
                           Id = r.Helper.Id,
                           HelperName = r.Helper.MemberName,
                           HelperEmail = r.Helper.Email,
                           SlackId = r.Helper.SlackId
                       },
                       ReqCar = r.ReqCar == null ? null : new ReqHelpCarDto
                       {
                           Id = r.ReqCar.Id,
                           CarNumber = r.ReqCar.CarNumber
                       }
                   })
                   .Where(x => x.Id == newReqHelp.Id)
                   .ToListAsync();

                // 요청자 정보 추출
                string requestName = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.HelpRequesterName ?? "Unknown";
                string requestEmail = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.RequesterEmail ?? "Unknown Email";
                string requestSlackId = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.SlackId ?? "Unknown Slack ID";
                string requestCarNumber = returnNewReqHelps.FirstOrDefault()?.ReqCar?.CarNumber ?? "Unknown Car Number";

                if ((string.IsNullOrEmpty(requestSlackId) || requestSlackId == "Unknown Slack ID") && requestEmail != "Unknown Email")
                {
                    SlackUserByEmail? slackUser = await _slackNotifier.FindUserByEmailAsync(requestEmail);
                    if (slackUser != null && !string.IsNullOrEmpty(slackUser.Id))
                    {
                        requestSlackId = slackUser.Id;
                    }
                }
                JObject? resultSlaclSendMessage = null;
                if (requestSlackId != "Unknown Slack ID")
                {
                    resultSlaclSendMessage = await _slackNotifier.SendMessageAsync($"<@{requestSlackId}>의 주차 등록을 도와주세요! 차량번호:{requestCarNumber} ");
                }
                else
                {
                    resultSlaclSendMessage = await _slackNotifier.SendMessageAsync($"주차 등록을 도와주세요! 차량번호:{requestCarNumber} ");
                }

                if (resultSlaclSendMessage != null && Convert.ToBoolean(resultSlaclSendMessage["ok"])) // 슬랙 메시지 전송 성공시
                {
                    if (!string.IsNullOrEmpty(resultSlaclSendMessage["ts"]?.ToString()))
                    {
                        newReqHelp.SlackThreadTs = resultSlaclSendMessage["ts"]?.ToString();
                        await _context.SaveChangesAsync();
                        ReqHelpDto reqHelpDto = returnNewReqHelps.First();
                        reqHelpDto.UpdateSlackThreadTs = newReqHelp.SlackThreadTs;
                    }
                    return Ok(returnNewReqHelps);
                }
                else
                {
                    Console.WriteLine("슬랙 메시지 전송 실패: " + resultSlaclSendMessage?.ToString());
                    return Ok(returnNewReqHelps);
                }
                //Azure 서버도 넣어야하고 할께 많은데 시간은 없고 용준아 잘좀하자
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutRequestHelp(int id, [FromBody] RequestHelpPutParam query)
        {
            var reqHelp = await _context.ReqHelps.FirstOrDefaultAsync(x => x.Id == id);

            if (reqHelp == null)
            {
                return NotFound("요청이 존재하지 않습니다.");
            }

            try
            {
                reqHelp.Status = query.Status ?? reqHelp.Status;
                reqHelp.HelperMemId = query.HelperMemId;
                reqHelp.HelpDate = query.HelpDate ?? reqHelp.HelpDate;
                reqHelp.ConfirmDate = query.ConfirmDate ?? reqHelp.ConfirmDate;
                await _context.SaveChangesAsync();

                var updateReqHelps = await _context.ReqHelps
                   .Include(r => r.HelpRequester)
                   .Include(r => r.Helper)
                   .Include(r => r.ReqCar)
                    .Select(r => new ReqHelpDto
                    {
                        Id = r.Id,
                        ReqDate = r.ReqDate,
                        HelpDate = r.HelpDate,
                        Status = r.Status,
                        HelpRequester = new HelpRequesterDto
                        {
                            Id = r.HelpRequester.Id,
                            HelpRequesterName = r.HelpRequester.MemberName,
                            SlackId = r.HelpRequester.SlackId,
                            RequesterEmail = r.HelpRequester.Email
                        },
                        Helper = r.Helper == null ? null : new HelperDto
                        {
                            Id = r.Helper.Id,
                            HelperName = r.Helper.MemberName,
                            HelperEmail = r.Helper.Email,
                            SlackId = r.Helper.SlackId
                        },
                        ReqCar = r.ReqCar == null ? null : new ReqHelpCarDto
                        {
                            Id = r.ReqCar.Id,
                            CarNumber = r.ReqCar.CarNumber
                        }
                    }).Where(x => x.Id == id)
                   .ToListAsync();

                // 요청자 정보 추출
                string requestName = updateReqHelps.FirstOrDefault()?.HelpRequester?.HelpRequesterName ?? "Unknown";
                string requestEmail = updateReqHelps.FirstOrDefault()?.HelpRequester?.RequesterEmail ?? "Unknown Email";
                string requestSlackId = updateReqHelps.FirstOrDefault()?.HelpRequester?.SlackId ?? "Unknown Slack ID";
                string requestCarNumber = updateReqHelps.FirstOrDefault()?.ReqCar?.CarNumber ?? "Unknown Car Number";

                string helperName = updateReqHelps.FirstOrDefault()?.Helper?.HelperName ?? "Unknown Helper";
                string helperEmail = updateReqHelps.FirstOrDefault()?.Helper?.HelperEmail ?? "Unknown Helper Email";
                string helperSlackId = updateReqHelps.FirstOrDefault()?.Helper?.SlackId ?? "Unknown Helper Slack ID";

                if ((string.IsNullOrEmpty(requestSlackId) || requestSlackId == "Unknown Slack ID") && requestEmail != "Unknown Email")
                {
                    SlackUserByEmail? slackUser = await _slackNotifier.FindUserByEmailAsync(requestEmail);
                    if (slackUser != null && !string.IsNullOrEmpty(slackUser.Id))
                    {
                        requestSlackId = slackUser.Id;
                    }
                }

                if ((string.IsNullOrEmpty(helperSlackId) || helperSlackId == "Unknown Helper Slack ID") && helperEmail != "Unknown Helper Email")
                {
                    SlackUserByEmail? slackUser = await _slackNotifier.FindUserByEmailAsync(helperEmail);
                    if (slackUser != null && !string.IsNullOrEmpty(slackUser.Id))
                    {
                        helperSlackId = slackUser.Id;
                    }
                }

                //if (query.Status.HasValue)
                //{
                //    switch ((CarHelpStatus)query.Status)
                //    {
                //        case CarHelpStatus.Completed: //주차등록이 완료 
                //            if (requestSlackId != "Unknown Slack ID" && helperSlackId != "Unknown Helper Slack ID") //slackID를 둘다 찾은경우
                //            {
                //                await _slackNotifier.SendMessageAsync($"<@{helperSlackId}>님이 <@{requestSlackId}> 의 차량({requestCarNumber}) 주차 등록을 완료했습니다! ", reqHelp.SlackThreadTs);
                //            }
                //            else if (requestSlackId != "Unknown Slack ID" && helperSlackId == "Unknown Helper Slack ID") //요청자의 SlackID만 존재할경우
                //            {
                //                await _slackNotifier.SendMessageAsync($"@channel <@{requestSlackId}>의 차량({requestCarNumber})을 {helperName}님이 주차 등록을 완료했습니다!!", reqHelp.SlackThreadTs);
                //            }
                //            else
                //            {
                //                await _slackNotifier.SendMessageAsync($"@channel 차량({requestCarNumber})주차등록을 {helperName}님이 주차 등록을 완료했습니다!!", reqHelp.SlackThreadTs);
                //            }
                //            break;

                //        case CarHelpStatus.Check:
                //            if (requestSlackId != "Unknown Slack ID" && helperSlackId != "Unknown Helper Slack ID") //slackID를 둘다 찾은경우
                //            {
                //                await _slackNotifier.SendMessageAsync($"<@{requestSlackId}> 의 차량({requestCarNumber})을 <@{helperSlackId}>님이 주차 등록 요청을 확인했어요! ", reqHelp.SlackThreadTs);
                //            }
                //            else if (requestSlackId != "Unknown Slack ID" && helperSlackId == "Unknown Helper Slack ID") //요청자의 SlackID만 존재할경우
                //            {
                //                await _slackNotifier.SendMessageAsync($"@channel <@{requestSlackId}> 의 차량({requestCarNumber})을 {helperName}님이 주차 등록 요청을 확인했어요!", reqHelp.SlackThreadTs);
                //            }
                //            else
                //            {
                //                await _slackNotifier.SendMessageAsync($"@channel 차량({requestCarNumber}) 주차등록을 {helperName}님이 주차 등록 요청을 확인했습니다!", reqHelp.SlackThreadTs);
                //            }
                //            break;
                //    }
                //}

                return Ok(updateReqHelps);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRequestHelp(int id)
        {
            var reqHelp = await _context.ReqHelps.FirstOrDefaultAsync(x => x.Id == id);

            if (reqHelp == null)
            {
                return NotFound("요청이 존재하지 않습니다.");
            }

            try
            {
                _context.ReqHelps.Remove(reqHelp);
                await _context.SaveChangesAsync();
                return Ok(reqHelp);
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
