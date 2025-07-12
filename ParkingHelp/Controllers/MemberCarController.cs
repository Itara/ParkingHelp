using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MemberCarController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MemberCarController(AppDbContext context)
        {
            _context = context;
        }
        [HttpGet()]
        public async Task<IActionResult> GetMemberCar([FromQuery] MemberCarGetParam param)
        {
            try
            {
                var query = _context.MemberCars.AsQueryable();
                // 조건이 있는 것만 차례대로 붙임
                query = string.IsNullOrWhiteSpace(param.CarNumber)
                    ? query
                    : query.Where(m => m.CarNumber.Contains(param.CarNumber));

                query = param.MemberId == null
                    ? query
                    : query.Where(m => m.MemberId == param.MemberId);

                var result = await query.ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                JObject result = new JObject
                {
                    { "Result", "Fail" },
                    { "ErrMsg",ex.Message }
                };
                return BadRequest(result.ToString());
            }
        }

    }
}
