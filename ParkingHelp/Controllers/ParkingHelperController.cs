using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.DTO;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using System.Linq;
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

        /// <summary>
        /// 주차등록 요청 조회
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("RequestHelp")]
        public async Task<IActionResult> GetRequestHelp([FromQuery] RequestHelpGetParam query)
        {
            try
            {
                DateTime nowKST = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"));
                DateTime startOfToday = nowKST.Date; //
                DateTime endOfToday = startOfToday.AddDays(1).AddSeconds(-1);
                
                DateTime fromDate = query.FromReqDate ?? startOfToday;
                DateTime toDate = query.ToReqDate ?? endOfToday;
                var reqHelps = await _context.ReqHelps
                    .Include(r => r.HelpRequester)
                    .Include(r => r.Helper)
                    .Include(r => r.ReqCar)
                     .Select(r => new ReqHelpDto
                     {
                         Id = r.Id,
                         ReqDate = r.ReqDate,
                         HelpDate = r.HelpDate,
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

        [HttpPost("RequestHelp")]
        public async Task<IActionResult> PostRequestHelp([FromBody] RequestHelpPostParam query)
        {
            try
            {
                var newReqHelp = new ReqHelp
                {
                    HelpReqMemId = query.HelpReqMemId ?? 0,
                    Status = 0,
                    ReqCarId = query.CarId,
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
            var reqHelp = await _context.ReqHelps.Include(x => x.Helper).FirstOrDefaultAsync(x => x.Id == id && x.HelperMemId == query.HelperMemId);

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
