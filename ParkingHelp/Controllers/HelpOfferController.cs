using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;

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
        public async Task<IActionResult> GetHelpOfferList([FromQuery] RequestHelpGetParam query)
        {
            try
            {
                DateTimeOffset nowKST = DateTimeOffset.UtcNow;
                DateTimeOffset startOfToday = nowKST.Date; //
                DateTimeOffset endOfToday = startOfToday.AddDays(1).AddSeconds(-1);

                DateTimeOffset fromDate = query.FromReqDate ?? startOfToday;
                DateTimeOffset toDate = query.ToReqDate ?? endOfToday;
                var reqHelps = await _context.ReqHelps
                    .Where(x => x.ReqDate >= fromDate && x.ReqDate <= toDate)
                    .OrderBy(x => x.ReqDate).ToListAsync();

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

        [HttpPost("HelpOffer")]
        public async Task<IActionResult> PostRequestHelp([FromBody] RequestHelpPostParam query)
        {
            try
            {
                var newHelpOffer = new HelpOfferModel
                {

                };
                _context.HelpOffers.Add(newHelpOffer);
                await _context.SaveChangesAsync();
                return Ok(newHelpOffer);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message);
                return BadRequest(jResult.ToString());
            }
        }

        [HttpPut("HelpOffer/{id}")]
        public async Task<IActionResult> PutHelpOffer(int id, [FromBody] RequestHelpDetailParam query)
        {
            var reqHelp = await _context.HelpOffers.FirstOrDefaultAsync(x => x.Id == id);

            if (reqHelp == null)
            {
                return NotFound("?? 존재하지 않습니다.");
            }

            try
            {

                await _context.SaveChangesAsync();
                return Ok(reqHelp);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message);
                return BadRequest(jResult.ToString());
            }
        }
        [HttpDelete("HelpOffer/{id}")]
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
                JObject jResult = GetErrorJobject(ex.Message);
                return BadRequest(jResult.ToString());
            }
        }


        private JObject GetErrorJobject(string errorMessage)
        {
            return new JObject
            {
                { "Result", "Error" },
                { "ErrorMsg", errorMessage }
            };
        }
    }
}
