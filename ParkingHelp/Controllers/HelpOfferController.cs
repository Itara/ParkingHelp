using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.DTO;
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
        public async Task<IActionResult> GetHelpOfferList([FromQuery] HelpOfferParam query)
        {
            try
            {
                var kstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
                var nowKST = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, kstTimeZone);
                var startOfTodayKST = new DateTimeOffset(nowKST.Date, kstTimeZone.GetUtcOffset(nowKST.Date));
                var endOfTodayKST = startOfTodayKST.AddDays(1).AddSeconds(-1);

                DateTimeOffset fromDate = query.FromReqDate ?? startOfTodayKST;
                DateTimeOffset toDate = query.ToReqDate ?? endOfTodayKST;

                var helpOfferList = await _context.HelpOffers
                    .Include(h => h.HelperMember)
                    .Include(h => h.RequestMember)
                    .Include(h => h.ReserveCar)
                    //.Where(x => x.ReqDate >= fromDate.UtcDateTime && x.ReqDate <= toDate.UtcDateTime)
                    .Select(h => new HelpOfferDTO
                    {
                        Id = h.HelperMemId,
                        HelpOfferDate = h.InsertDate,
                        RequestDate = (h.ReqDate ?? DateTimeOffset.MinValue).ToUniversalTime(),
                        Status = h.Status,
                        HelpOffName = h.HelperMember.MemberName,

                        HelpRequester = new HelpRequesterDto
                        {
                            Id = h.RequestMember.Id,
                            HelpRequesterName = h.RequestMember.MemberName,
                            RequesterEmail = h.RequestMember.Email,
                            ReqHelpCar = h.ReserveCar != null ? new ReqHelpCarDto
                            {
                                Id = h.ReserveCar.Id,
                                CarNumber = h.ReserveCar.CarNumber
                            } : null
                        },
                    })
                    .ToListAsync();

                return Ok(helpOfferList);
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
