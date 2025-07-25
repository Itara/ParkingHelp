﻿using log4net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Elfie.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
using ParkingHelp.Logging;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;
using System.Linq;
using static ParkingHelp.DTO.HelpRequesterDto;
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
                var kstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
                var nowKST = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kstTimeZone);
                var startOfTodayKST = new DateTimeOffset(nowKST.Date, kstTimeZone.GetUtcOffset(nowKST.Date));
                var endOfTodayKST = startOfTodayKST.AddDays(1).AddSeconds(-1);

                DateTimeOffset fromDate = query.FromReqDate ?? startOfTodayKST;
                DateTimeOffset toDate = query.ToReqDate ?? endOfTodayKST;

                var reqHelpsQuery = _context.ReqHelps
                    .Include(r => r.HelpReqMember)
                    .ThenInclude(m => m.Cars)
                    .Include(r => r.HelpDetails)
                   .Where(x => x.ReqDate >= fromDate.UtcDateTime && x.ReqDate <= toDate.UtcDateTime);

                if (query.HelpReqMemId.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.HelpReqMember.Id == query.HelpReqMemId);
                }

                if (query.Status.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.Status == query.Status);
                }
                if (query.ReqDetailStatus.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.HelpDetails.Any(d => d.ReqDetailStatus == query.ReqDetailStatus));
                }

                var reqHelps = await reqHelpsQuery
                .Select(r => new ReqHelpDto
                {
                    Id = r.Id,
                    ReqDate = r.ReqDate,
                    Status = r.Status,
                    TotalDisCount = r.DiscountTotalCount,
                    ApplyDisCount = r.DiscountApplyCount ?? 0,
                    HelpRequester = new HelpRequesterDto
                    {
                        Id = r.HelpReqMember.Id,
                        HelpRequesterName = r.HelpReqMember.MemberName,
                        RequesterEmail = r.HelpReqMember.Email,
                        ReqHelpCar = (r.HelpReqMember.Cars != null && r.HelpReqMember.Cars.Any())
                        ? new ReqHelpCarDto
                        {
                            Id = r.HelpReqMember.Cars.First().Id,
                            CarNumber = r.HelpReqMember.Cars.First().CarNumber
                        } : null
                    },
                    HelpDetails = r.HelpDetails
                    .Select(d => new ReqHelpDetailDto
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        InsertDate = d.InsertDate,
                        SlackThreadTs = d.SlackThreadTs,
                        Helper = d.HelperMember == null ? null : new HelpMemberDto
                        {
                            Id = d.HelperMember.Id,
                            Name = d.HelperMember.MemberName,
                            Email = d.HelperMember.Email,
                            SlackId = d.HelperMember.SlackId
                        }
                    }).ToList()
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
                var newReqHelp = new ReqHelpModel
                {
                    HelpReqMemId = query.HelpReqMemId ?? 0,
                    Status = 0,
                    ReqDate = DateTimeOffset.UtcNow,
                    DiscountTotalCount = query.TotalDisCount,
                    DiscountApplyCount = 0
                };

                _context.ReqHelps.Add(newReqHelp);
                await _context.SaveChangesAsync();

                List<ReqHelpDetailModel> reqHelpDetailModels = new();
                for (int i = 0; i < query.TotalDisCount; i++)
                {
                    reqHelpDetailModels.Add(new ReqHelpDetailModel
                    {
                        Req_Id = newReqHelp.Id,
                        InsertDate = DateTimeOffset.UtcNow,
                        ReqDetailStatus = 0
                    });
                }

                _context.ReqHelpsDetail.AddRange(reqHelpDetailModels);


                await _context.SaveChangesAsync();

                var returnNewReqHelps = await _context.ReqHelps
                    .Include(r => r.HelpReqMember)
                    .ThenInclude(m => m.Cars)
                    .Include(r => r.HelpDetails)
                   .Select(r => new ReqHelpDto
                   {
                       Id = r.Id,
                       ReqDate = r.ReqDate,
                       Status = r.Status,
                       TotalDisCount = r.DiscountTotalCount,
                       ApplyDisCount = 0,
                       HelpRequester = new HelpRequesterDto
                       {
                           Id = r.HelpReqMember.Id,
                           HelpRequesterName = r.HelpReqMember.MemberName,
                           RequesterEmail = r.HelpReqMember.Email,
                           ReqHelpCar = r.HelpReqMember.Cars.Select(c => new ReqHelpCarDto
                           {
                               Id = c.Id,
                               CarNumber = c.CarNumber
                           }).FirstOrDefault()
                       },
                       HelpDetails = r.HelpDetails
                    .Select(d => new ReqHelpDetailDto
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        InsertDate = d.InsertDate,
                        SlackThreadTs = d.SlackThreadTs
                    }).ToList()
                   })
                   .Where(x => x.Id == newReqHelp.Id)
                   .ToListAsync();

                // 요청자 정보 추출
                string requestName = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.HelpRequesterName ?? "Unknown";
                string requestEmail = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.RequesterEmail ?? "Unknown Email";
                string requestSlackId = returnNewReqHelps.FirstOrDefault()?.HelpRequester?.SlackId ?? "Unknown Slack ID";
                string requestCarNumber = returnNewReqHelps.FirstOrDefault()?.HelpRequester.ReqHelpCar?.CarNumber ?? "Unknown Car Number";

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
                    // resultSlaclSendMessage = await _slackNotifier.SendMessageAsync($"<@{requestSlackId}>의 주차 등록을 도와주세요! 차량번호:{requestCarNumber} ");
                }
                else
                {
                    // resultSlaclSendMessage = await _slackNotifier.SendMessageAsync($"주차 등록을 도와주세요! 차량번호:{requestCarNumber} ");
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
            var reqHelp = await _context.ReqHelps
                .Include(r => r.HelpReqMember)
                .ThenInclude(m => m.Cars)
                .Include(r => r.HelpDetails).FirstOrDefaultAsync(x => x.Id == id);

            if (reqHelp == null)
            {
                return NotFound("요청이 존재하지 않습니다.");
            }
            try
            {
                int requestTotalDisCountCount = reqHelp.DiscountTotalCount;
                if (query.RequestHelpDetail != null)
                {
                    foreach (RequestHelpDatailPutParam requestDetail in query.RequestHelpDetail)
                    {
                        var existingDetail = reqHelp.HelpDetails.FirstOrDefault(x => x.Id == requestDetail.Id);
                        if (existingDetail != null) //세부내용
                        {
                            //HelperMemberId 가 0으로 들어오면 삭제
                            existingDetail.HelperMemberId = requestDetail.HelperMemId.HasValue ? (requestDetail.HelperMemId == 0 ? null : requestDetail.HelperMemId) : existingDetail.HelperMemberId;
                            existingDetail.DiscountApplyDate = requestDetail.DiscountApplyDate ?? existingDetail.DiscountApplyDate;
                            existingDetail.ReqDetailStatus = requestDetail.Status ?? existingDetail.ReqDetailStatus;
                            existingDetail.DiscountApplyType = requestDetail.DiscountApplyType ?? existingDetail.DiscountApplyType;

                            if (existingDetail.ReqDetailStatus == ReqDetailStatus.Waiting && existingDetail.HelperMemberId.HasValue && existingDetail.HelperMemberId != 0)
                            {
                                existingDetail.ReqDetailStatus = ReqDetailStatus.Check;
                            }
                        }
                    }
                }
                reqHelp.Status = query.Status ?? reqHelp.Status;
                _context.ReqHelps.Update(reqHelp);

                await _context.SaveChangesAsync();

                var updateReqHelps = await _context.ReqHelps
                .Where(r => r.Id == id)
                .Include(r => r.HelpReqMember)
                .Include(r => r.ReqCar)
                .Include(r => r.HelpDetails)
                .Select(r => new ReqHelpDto
                {
                    Id = r.Id,
                    ReqDate = r.ReqDate,
                    Status = r.Status,
                    ApplyDisCount = r.DiscountApplyCount,
                    TotalDisCount = r.DiscountTotalCount,
                    HelpRequester = new HelpRequesterDto
                    {
                        Id = r.HelpReqMember.Id,
                        HelpRequesterName = r.HelpReqMember.MemberName,
                        RequesterEmail = r.HelpReqMember.Email,
                        SlackId = r.HelpReqMember.SlackId,
                        ReqHelpCar = new ReqHelpCarDto
                        {
                            Id = r.HelpReqMember.Cars.First().Id,
                            CarNumber = r.HelpReqMember.Cars.First().CarNumber
                        }
                    }
                   ,
                    HelpDetails = r.HelpDetails.Select(d => new ReqHelpDetailDto
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        DiscountApplyType = d.DiscountApplyType,
                        InsertDate = d.InsertDate,
                        Helper = d.HelperMember == null ? null : new HelpMemberDto
                        {
                            Id = d.HelperMember.Id,
                            Name = d.HelperMember.MemberName,
                            Email = d.HelperMember.Email,
                            SlackId = d.HelperMember.SlackId
                        },
                        SlackThreadTs = d.SlackThreadTs
                    }).ToList()
                })
                .FirstOrDefaultAsync();

                // 요청자 정보 추출
                string requestName = updateReqHelps?.HelpRequester?.HelpRequesterName ?? "Unknown";
                string requestEmail = updateReqHelps?.HelpRequester?.RequesterEmail ?? "Unknown Email";
                string requestSlackId = updateReqHelps?.HelpRequester?.SlackId ?? "Unknown Slack ID";
                string requestCarNumber = updateReqHelps?.HelpRequester.ReqHelpCar?.CarNumber ?? "Unknown Car Number";

                if ((string.IsNullOrEmpty(requestSlackId) || requestSlackId == "Unknown Slack ID") && requestEmail != "Unknown Email")
                {
                    SlackUserByEmail? slackUser = await _slackNotifier.FindUserByEmailAsync(requestEmail);
                    if (slackUser != null && !string.IsNullOrEmpty(slackUser.Id))
                    {
                        requestSlackId = slackUser.Id;
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

        [HttpGet("ReqHelpDetail/{RequestId}")]
        public async Task<IActionResult> GetRequestHelpDetail(int RequestId)
        {
            try
            {
                var reqHelpDetail = await _context.ReqHelpsDetail
                    .Include(r => r.ReqHelps)
                    .Include(r => r.HelperMember)
                    .Where(x => x.Req_Id == RequestId)
                    .Select(r => new ReqHelpDetailDto
                    {
                        Id = r.Id,
                        ReqDetailStatus = r.ReqDetailStatus,
                        DiscountApplyDate = r.DiscountApplyDate,
                        DiscountApplyType = r.DiscountApplyType,
                        InsertDate = r.InsertDate,
                        SlackThreadTs = r.SlackThreadTs,
                        Helper = r.HelperMember == null ? null : new HelpMemberDto
                        {
                            Id = r.HelperMember.Id,
                            Name = r.HelperMember.MemberName,
                            Email = r.HelperMember.Email,
                        },
                    })
                    .OrderBy(x => x.Id)
                    .ToListAsync();
                return Ok(reqHelpDetail);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }


        [HttpPut("ReqHelpDetail")]
        public async Task<IActionResult> PutRequestHelpDetailMultiUpdate([FromBody] RequestHelpDetailMultiUpdatePutParam param)
        {
            try
            {
                int updateReqId = param.ReqId;
                var reqHelp = await _context.ReqHelps
                .Include(r => r.HelpReqMember)
                .ThenInclude(m => m.Cars)
                .Include(r => r.HelpDetails).FirstOrDefaultAsync(x => x.Id == updateReqId);

                if (reqHelp == null)
                {
                    JObject jResult = GetErrorJobject($"id : {updateReqId} 값에 해당되는 요청 값이 없습니다 ", "");
                    return NotFound(jResult.ToString());
                }
                else
                {
                    reqHelp.DiscountApplyCount = param.DiscountApplyCount ?? reqHelp.DiscountApplyCount;
                    var helpDetailModels = reqHelp.HelpDetails
                      .Where(x => x.ReqDetailStatus == param.UpdateTargetReqDetailStatus &&
                                  (param.UpdateTargetIdList == null || param.UpdateTargetIdList.Count == 0 || param.UpdateTargetIdList.Contains(x.Id)))
                      .OrderBy(x => x.Id)
                      .Take(param.UpdateTargetCount > 0 ? param.UpdateTargetCount.Value : int.MaxValue)
                      .ToList();

                    foreach (var ReqHelpDetailModel in helpDetailModels)
                    {
                        ReqHelpDetailModel.DiscountApplyDate = param.RequestHelpDatailPutParam.DiscountApplyDate ?? ReqHelpDetailModel.DiscountApplyDate;
                        ReqHelpDetailModel.DiscountApplyType = param.RequestHelpDatailPutParam.DiscountApplyType ?? ReqHelpDetailModel.DiscountApplyType;
                        ReqHelpDetailModel.ReqDetailStatus = param.RequestHelpDatailPutParam.Status ?? ReqHelpDetailModel.ReqDetailStatus;

                        if (param.RequestHelpDatailPutParam.HelperMemId.HasValue)
                        {
                            ReqHelpDetailModel.HelperMemberId = param.RequestHelpDatailPutParam.HelperMemId == 0 ? null : param.RequestHelpDatailPutParam.HelperMemId;
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                var updateReqHelps = await _context.ReqHelps
               .Where(r => r.Id == updateReqId)
               .Include(r => r.HelpReqMember)
               .Include(r => r.ReqCar)
               .Include(r => r.HelpDetails)
               .Select(r => new ReqHelpDto
               {
                   Id = r.Id,
                   ReqDate = r.ReqDate,
                   Status = r.Status,
                   ApplyDisCount = r.DiscountApplyCount,
                   TotalDisCount = r.DiscountTotalCount,
                   HelpRequester = new HelpRequesterDto
                   {
                       Id = r.HelpReqMember.Id,
                       HelpRequesterName = r.HelpReqMember.MemberName,
                       RequesterEmail = r.HelpReqMember.Email,
                       SlackId = r.HelpReqMember.SlackId,
                       ReqHelpCar = new ReqHelpCarDto
                       {
                           Id = r.HelpReqMember.Cars.First().Id,
                           CarNumber = r.HelpReqMember.Cars.First().CarNumber
                       }
                   }
                  ,
                   HelpDetails = r.HelpDetails.Select(d => new ReqHelpDetailDto
                   {
                       Id = d.Id,
                       ReqDetailStatus = d.ReqDetailStatus,
                       DiscountApplyDate = d.DiscountApplyDate,
                       DiscountApplyType = d.DiscountApplyType,
                       InsertDate = d.InsertDate,
                       Helper = d.HelperMember == null ? null : new HelpMemberDto
                       {
                           Id = d.HelperMember.Id,
                           Name = d.HelperMember.MemberName,
                           Email = d.HelperMember.Email,
                           SlackId = d.HelperMember.SlackId
                       },
                       SlackThreadTs = d.SlackThreadTs
                   }).ToList()
               })
               .FirstOrDefaultAsync();
                return Ok(updateReqHelps);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }


        [HttpPut("ReqHelpDetail/{RequestDetailId}")]
        public async Task<IActionResult> PutRequestHelpDetail(int RequestDetailId, [FromQuery] RequestHelpDetailPutParam param)
        {
            Logs.Info($"PutRequestHelpDetail Id {RequestDetailId} , param : {param.ToString()}");
            try
            {
                var updateTarget = await _context.ReqHelpsDetail
                    .Include(r => r.ReqHelps)
                    .Include(r => r.HelperMember)
                    .FirstOrDefaultAsync(x => x.Id == RequestDetailId);

                if (updateTarget == null)
                {
                    JObject jResult = GetErrorJobject($"id : {RequestDetailId} 값에 해당되는 요청 값이 없습니다 ", "");
                    return NotFound(jResult.ToString());
                }
                else
                {
                    ReqHelpModel? rephelp = _context.ReqHelps.Where(x => x.Id == updateTarget.Req_Id).FirstOrDefault();
                    if (rephelp == null)
                    {
                        JObject jResult = GetErrorJobject($"id : {updateTarget.Req_Id} 값에 해당되는 요청 값이 없습니다 ", "");
                        return NotFound(jResult.ToString());
                    }

                    updateTarget.HelperMemberId = param.HelperMemId.HasValue ? (param.HelperMemId == 0 ? null : param.HelperMemId) : updateTarget.HelperMemberId;
                    updateTarget.DiscountApplyDate = param.DisCountApplyDate.HasValue ? param.DisCountApplyDate.Value.ToUniversalTime() : updateTarget.DiscountApplyDate;
                    updateTarget.DiscountApplyType = param.DisCountApplyType.HasValue ? param.DisCountApplyType.Value : updateTarget.DiscountApplyType;
                    if (param.ReqDetailStatus.HasValue)
                    {
                        updateTarget.ReqDetailStatus = param.ReqDetailStatus.Value;
                    }

                    _context.ReqHelpsDetail.Update(updateTarget);
                    await _context.SaveChangesAsync(); //요청 세부사항 DB에 반영

                    int applyCount = _context.ReqHelpsDetail.Where(x => x.Req_Id == updateTarget.Req_Id && x.ReqDetailStatus != ReqDetailStatus.Waiting).Count(); //현재 남아있는 ReqHelpDetail의 개수 중 적용된 개수를 가져옴
                    rephelp.DiscountApplyCount = applyCount;
                    if(rephelp.DiscountTotalCount == applyCount)
                    {
                        rephelp.Status = HelpStatus.Completed; //모든 요청이 완료되면 상태를 Completed로 변경
                    }
                    else if(applyCount != 0 && applyCount < rephelp.DiscountTotalCount)
                    {
                        rephelp.Status = HelpStatus.Check;
                    }
                    else
                    {
                        rephelp.Status = HelpStatus.Waiting; //1건도 적용되지 않은 상태
                    }

                    await _context.SaveChangesAsync();
                }

                var returnReqHelp = await _context.ReqHelps.Where(r => r.Id == updateTarget.Req_Id)
                                        .Include(r => r.HelpReqMember)
                                            .ThenInclude(m => m.Cars)
                                        .Include(r => r.HelpDetails)
                                        .Select(r => new ReqHelpDto
                                        {
                                            Id = r.Id,
                                            ReqDate = r.ReqDate,
                                            Status = r.Status,
                                            TotalDisCount = r.DiscountTotalCount,
                                            ApplyDisCount = r.DiscountApplyCount, // 적용 수량은 추후 계산 or 외부 값
                                            HelpRequester = new HelpRequesterDto
                                            {
                                                Id = r.HelpReqMember.Id,
                                                HelpRequesterName = r.HelpReqMember.MemberName,
                                                RequesterEmail = r.HelpReqMember.Email,
                                                ReqHelpCar = r.HelpReqMember.Cars == null ? null : new ReqHelpCarDto
                                                {
                                                    Id = r.HelpReqMember.Cars.First().Id,
                                                    CarNumber = r.HelpReqMember.Cars.First().CarNumber
                                                }
                                            },
                                            HelpDetails = r.HelpDetails.Where(d => d.Id == RequestDetailId).Select(d => new ReqHelpDetailDto
                                            {
                                                Id = d.Id,
                                                ReqDetailStatus = d.ReqDetailStatus,
                                                DiscountApplyDate = d.DiscountApplyDate,
                                                DiscountApplyType = d.DiscountApplyType,
                                                InsertDate = d.InsertDate,
                                                SlackThreadTs = d.SlackThreadTs,
                                                Helper = d.HelperMember == null ? null : new HelpMemberDto
                                                {
                                                    Id = d.HelperMember.Id,
                                                    Name = d.HelperMember.MemberName,
                                                    Email = d.HelperMember.Email,
                                                    SlackId = d.HelperMember.SlackId
                                                }
                                            }).ToList()
                                        }).ToListAsync();

                return Ok(returnReqHelp);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }

        [HttpDelete("ReqHelpDetail/{RequestDetailId}")]
        public async Task<IActionResult> DelteRequestHelpDetail(int RequestDetailId)
        {
            //_Logger.Info($"DelteRequestHelpDetail Id {RequestDetailId}");
            try
            {
                JObject returnJob = new JObject();
                var deleteReqHelpDetail = _context.ReqHelpsDetail.Where(x => x.Id == RequestDetailId).FirstOrDefault();
                if (deleteReqHelpDetail == null)
                {
                    return NotFound(GetErrorJobject($"해당 요청세부 내역(ID: {RequestDetailId})이 존재하지 않습니다.", "").ToString());
                }

                int reqId = deleteReqHelpDetail.Req_Id;

                _context.ReqHelpsDetail.Remove(deleteReqHelpDetail);

                //DB반영
                await _context.SaveChangesAsync();

                int totalCount = _context.ReqHelpsDetail.Where(x => x.Req_Id == reqId).Count(); //현재 남아있는 ReqHelpDetail의 개수를 가져옴
                int applyCount = _context.ReqHelpsDetail.Where(x => x.Req_Id == reqId && x.ReqDetailStatus != ReqDetailStatus.Waiting).Count(); //현재 남아있는 ReqHelpDetail의 개수 중 적용된 개수를 가져옴

                ReqHelpModel? updateReqHelp = await _context.ReqHelps.FirstOrDefaultAsync(x => x.Id == reqId);
                if (totalCount > 0 && updateReqHelp != null)
                {
                    //삭제이후 요청의 총갯수를 다시 적용한다.
                    updateReqHelp.DiscountTotalCount = totalCount;
                    updateReqHelp.DiscountApplyCount = applyCount;
                    _context.ReqHelps.Update(updateReqHelp);
                }
                else if (totalCount == 0 && updateReqHelp != null)//ReqHelpDetail이 하나도 남아있지 않으면 ReqHelp를 삭제
                {
                    _context.ReqHelps.Remove(updateReqHelp);
                    await _context.SaveChangesAsync();
                    return Ok(new List<ReqHelpDto>());
                }
                else
                {
                    JObject jResult = GetErrorJobject("updateReqHelp Is Null", "InnerException is Null");
                    return BadRequest(jResult.ToString());
                }

                //DB반영
                await _context.SaveChangesAsync();

                var latestReqHelp = _context.ReqHelps.Where(x => x.Id == reqId)
                 .Include(r => r.HelpReqMember).ThenInclude(m => m.Cars)
                 .Include(r => r.HelpDetails).ThenInclude(r => r.HelperMember)
                 .FirstOrDefault();
             
                if(latestReqHelp == null)
                {
                    return Ok();
                }

                var returnReqHelp = new ReqHelpDto
                {
                    Id = latestReqHelp.Id,
                    ReqDate = latestReqHelp.ReqDate,
                    Status = latestReqHelp.Status,
                    TotalDisCount = totalCount,
                    ApplyDisCount = latestReqHelp.DiscountApplyCount,
                    HelpRequester = new HelpRequesterDto
                    {
                        Id = latestReqHelp.HelpReqMember.Id,
                        HelpRequesterName = latestReqHelp.HelpReqMember.MemberName,
                        RequesterEmail = latestReqHelp.HelpReqMember.Email,
                        ReqHelpCar = latestReqHelp.ReqCar != null && latestReqHelp.HelpReqMember.Cars?.Any() == true
                        ? new ReqHelpCarDto
                        {
                            Id = latestReqHelp.HelpReqMember.Cars.First().Id,
                            CarNumber = latestReqHelp.HelpReqMember.Cars.First().CarNumber
                        }
                        : null
                    },
                    HelpDetails = latestReqHelp.HelpDetails.Select(d => new ReqHelpDetailDto
                    {
                        Id = d.Id,
                        ReqDetailStatus = d.ReqDetailStatus,
                        DiscountApplyDate = d.DiscountApplyDate,
                        DiscountApplyType = d.DiscountApplyType,
                        InsertDate = d.InsertDate,
                        SlackThreadTs = d.SlackThreadTs,
                        Helper = d.HelperMember == null ? null : new HelpMemberDto
                        {
                            Id = d.HelperMember.Id,
                            Name = d.HelperMember.MemberName,
                            Email = d.HelperMember.Email,
                            SlackId = d.HelperMember.SlackId
                        }
                    }).ToList()
                };
                return Ok(returnReqHelp);

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
