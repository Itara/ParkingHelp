using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
    public class ParkingHelperController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ParkingHelperController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("RequestHelp")]
        public async Task<IActionResult> GetRequestHelp([FromQuery] RequestHelpGetParam query)
        {
            try
            {
                DateTime fromDate = query.FromReqDate ?? DateTime.MinValue;
                DateTime toDate = query.ToReqDate ?? DateTime.MaxValue;
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

        [HttpPost("RequestHelp")]
        public async Task<IActionResult> PostRequestHelp([FromBody] RequestHelpPostParam query)
        {
            try
            {
               var newReqHelp = new ReqHelp
                {
                    HelpReqMemId = query.HelpReqMemId ?? 0,
                    Status = query.Status ?? 0,
                    carNumber = query.CarNumber ?? string.Empty,
                    ReqDate = DateTime.UtcNow
                };
                _context.ReqHelps.Add(newReqHelp);
                await _context.SaveChangesAsync();
                return Ok(newReqHelp);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message);
                return BadRequest(jResult.ToString());
            }
        }

        [HttpPut("RequestHelp/{id}")]
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
                reqHelp.HelperMemId = query.HelperMemId ?? reqHelp.HelperMemId;
                reqHelp.HelpDate = query.HelpDate ?? reqHelp.HelpDate;
                await _context.SaveChangesAsync();
                return Ok(reqHelp);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message);
                return BadRequest(jResult.ToString());
            }
        }
        [HttpDelete("RequestHelp/{id}")]
        public async Task<IActionResult> PutRequestHelp(int id)
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
