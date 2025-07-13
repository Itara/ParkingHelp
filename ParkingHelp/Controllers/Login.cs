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
    public class Login : ControllerBase
    {

        private readonly AppDbContext _context;

        public Login(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost()]
        public IActionResult LoginUser([FromBody] LoginParam query)
        {
            var member = _context.Members.Include(m => m.Cars).FirstOrDefault(x => x.MemberLoginId == query.MemberLoginId);
            if (member == null)
            {
                JObject result = new JObject
                {
                    { "Result", "Login Fail" },
                    { "ErrMsg", "사용자를 찾을수없습니다" }
                };
                return NotFound(result.ToString());
            }
            else
            {
                return Ok(member);
            }
        }
    }
}
