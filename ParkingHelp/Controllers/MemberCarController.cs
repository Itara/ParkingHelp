using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

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
        [HttpPost()]
        public async Task<IActionResult> AddMemberCar([FromBody] MemberCarPostParam param)
        {

            try
            {
                var newCar = new MemberCar
                {
                    CarNumber = param.CarNumber,
                    MemberId = param.MemberId,
                    CreateDate = DateTime.UtcNow,
                    UpdateDate = DateTime.UtcNow
                };
                await _context.MemberCars.AddAsync(newCar);
                await _context.SaveChangesAsync();
                return Ok(newCar);
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMemberCar(int id)
        {
            try
            {
                JObject returnJob = new JObject();

                if (_context.MemberCars.Any(m => m.Id == id))
                {
                    _context.MemberCars.RemoveRange(_context.MemberCars.Where(m => m.Id == id));
                    await _context.SaveChangesAsync();

                    returnJob = new JObject
                {
                    { "Result", "Success" },
                    { "MemberId", id }
                };
                    return Ok(returnJob.ToString());
                }
                else
                {
                    returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrMsg", "사용자가 존재하지 않습니다" }
                };
                    return BadRequest(returnJob.ToString());
                }
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
