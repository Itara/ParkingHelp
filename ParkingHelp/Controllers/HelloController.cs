using Microsoft.AspNetCore.Mvc;

namespace ParkingHelp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HelloController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHello()
        {
            return Ok("안녕하세요! 이것은 REST API입니다.");
        }
    }
}
