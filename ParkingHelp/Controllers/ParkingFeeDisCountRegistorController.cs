using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParkingFeeDisCountRegistorController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ParkingFeeDisCountRegistorController(AppDbContext context, SlackOptions slackOptions)
        {
            _context = context;
        }

        [HttpPost()]
        public async Task<IActionResult> PostDiscountParkingFee([FromBody] ParkingDiscountFeePostParam query)
        {
            //ParkingDiscountBot.ParkingDiscount parkingDiscount = new ParkingDiscountBot.ParkingDiscount(_slackNotifier);
            //JObject result = await parkingDiscount.RegisterParkingDiscountAsync(query.CarNumber,query.NotifySlackAlarm ?? false);
            //if(result != null )
            //{
            //    if(result["Result"].ToString() == "OK")
            //    {
            //        return Ok(result.ToString());
            //    }
            //    else
            //    {
            //        await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
            //        return BadRequest(result.ToString());
            //    }
            //}
            //else
            //{
            //    result = new JObject();
            //    result.Add("Result", "Fail");
            //    result.Add("ReturnMessage", "할인권 요청중 오류가 발생했습니다.");
            //    await _slackNotifier.SendMessageAsync($"{result["ReturnMessage"].ToString()}", null);
            //    return BadRequest(result.ToString());
            //}
            return Ok("");
        }
    }
    
}
