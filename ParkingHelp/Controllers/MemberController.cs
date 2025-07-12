using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using System.Diagnostics;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MemberController(AppDbContext context)
        {
            _context = context;
        }
        [HttpGet("Members")]
        public async Task<IActionResult> GetMembers([FromQuery] MemberGetParam param)
        {
            string memberId = param.memberLoginId ?? string.Empty;
            string memberName = param.memberName ?? string.Empty;
            string carNumber = param.carNumber ?? string.Empty;

            try
            {
                var query = _context.Members.AsQueryable();

                // 조건이 있는 것만 차례대로 붙임
                query = string.IsNullOrWhiteSpace(param.memberLoginId)
                    ? query
                    : query.Where(m => m.MemberLoginId.Contains(param.memberLoginId));

                query = string.IsNullOrWhiteSpace(param.memberName)
                    ? query
                    : query.Where(m => m.MemberName.Contains(param.memberName));

                query = string.IsNullOrWhiteSpace(param.carNumber)
                   ? query
                   : query.Where(m => m.Cars.Any(mc => mc.CarNumber.Contains(carNumber)));

                var result = await query.Include(m => m.Cars).ToListAsync();

                return Ok(result);
            }
            catch(Exception ex)
            {
                JObject returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrorMsg", $"{ex.Message}" }
                };
                return BadRequest(returnJob.ToString());
            }
        }
        // POST: api/Member
        [HttpPost()]
        public async Task<IActionResult> AddNewMember([FromBody] MemberAddParam query)
        {
            try
            {
                var newMember = new Member
                {
                    MemberLoginId = query.memberLoginId,
                    Password = query.password,
                    MemberName = query.memberName
                };
                _context.Members.Add(newMember);
                await _context.SaveChangesAsync();
                var newCar = new MemberCar
                {
                    CarNumber = query.carNumber,
                    MemberId = newMember.Id,
                    CreateDate = DateTime.UtcNow,
                    UpdateDate = DateTime.UtcNow
                };
                _context.MemberCars.Add(newCar);
                await _context.SaveChangesAsync();
                return Ok(newMember);
            }
            catch (Exception ex)
            {
                JObject returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrorMsg",$"{ex.InnerException}"}
                 };
                return BadRequest(returnJob.ToString());
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMember(int id, [FromBody] MemberUpdateParam query)
        {
            JObject returnJob = new JObject();
            var member = await _context.Members.FindAsync(id);
            if (member == null)
            {
                returnJob = new JObject
                {
                    { "Result", "Error" },
                    { "ErrMsg", "사용자가 존재하지 않습니다" }
                };
                return BadRequest(returnJob.ToString());
            }
            member.Password = query.password ?? member.Password;
            member.MemberName = query.memberName ?? member.MemberName;
            _context.Entry(member).Property(m => m.CreateDate).IsModified = false;
            _context.Members.Update(member);
            await _context.SaveChangesAsync();
            returnJob = new JObject
            {
                { "Result", "Success" },
                { "MemberId", member.Id }
            };
            return Ok(returnJob.ToString());
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMember(int id)
        {
            JObject returnJob = new JObject();

            if (_context.Members.Any(m => m.Id == id))
            {
                _context.Members.RemoveRange(_context.Members.Where(m => m.Id == id));
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

    }
}
