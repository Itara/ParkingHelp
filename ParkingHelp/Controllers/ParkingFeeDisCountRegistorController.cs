using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
using ParkingHelp.Models;
using ParkingHelp.ParkingDiscountBot;
using ParkingHelp.SlackBot;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParkingFeeDisCountRegistorController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SlackNotifier _slackNotifier;


        public ParkingFeeDisCountRegistorController(AppDbContext context, SlackOptions slackOptions)
        {
            _context = context;
            _slackNotifier = new SlackNotifier(slackOptions);
        }

        [HttpPost()]
        public async Task<IActionResult> PostDiscountParkingFee([FromBody] ParkingDiscountFeePostParam query)
        {

            ParkingDiscountModel parkingDiscountModel = new ParkingDiscountModel(query.CarNumber, string.Empty,query.NotifySlackAlarm ?? false , false);
            JObject result = await ParkingDiscountManager.EnqueueAsync(parkingDiscountModel, DiscountJobType.ApplyDiscount, (int)DiscountJobPriority.High);

            if (result != null)
            {
                if (result["Result"].ToString() == "OK")
                {
                    await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                    return Ok(result.ToString());
                }
                else
                {
                    await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                    return BadRequest(result.ToString());
                }
            }
            else
            {
                result = new JObject();
                result.Add("Result", "Fail");
                result.Add("ReturnMessage", "할인권 요청중 오류가 발생했습니다.");
                //await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                return BadRequest(result.ToString());
            }
        }

        [HttpGet("CheckParkingFee")]
        public async Task<IActionResult> GetDiscountParkingFee([FromQuery] int MemberId)
        {
            var member = await _context.Members.Include(m => m.Cars).FirstOrDefaultAsync(m => m.Id == MemberId);
            JObject returnJob = null;
            if (member == null)
            {
                returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrMsg", "사용자가 존재하지 않습니다" }
                };
                return BadRequest(returnJob.ToString());
            }
            MemberDto memberDto = new MemberDto
            {
                Id = member.Id,
                MemberLoginId = member.MemberLoginId,
                Cars = member.Cars.Select(c => new MemberCarDTO
                {
                    Id= c.Id,
                    CarNumber = c.CarNumber,
                }).ToList()
            };
            ParkingDiscountModel parkingDiscountModel = new ParkingDiscountModel(memberDto.Cars.First().CarNumber, string.Empty);

            JObject result = await ParkingDiscountManager.EnqueueAsync(parkingDiscountModel, DiscountJobType.CheckFeeOnly);

            if (result != null)
            {
                if (result["Result"].ToString() == "OK")
                {
                    await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                    return Ok(result.ToString());
                }
                else
                {
                    await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                    return BadRequest(result.ToString());
                }
            }
            else
            {
                result = new JObject();
                result.Add("Result", "Fail");
                result.Add("ReturnMessage", "할인권 요청중 오류가 발생했습니다.");
                await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
                return BadRequest(result.ToString());
            }
        }
    }
    
}
