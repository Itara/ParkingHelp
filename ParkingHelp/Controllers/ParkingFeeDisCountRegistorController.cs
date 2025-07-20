using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using ParkingHelp.ParkingDiscountBot;
using ParkingHelp.SlackBot;

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
            
            JObject result = await PlaywrightManager.EnqueueAsync(query.CarNumber);
            
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
