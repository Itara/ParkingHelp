using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuGet.ProjectModel;
using ParkingHelp.DB;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using ParkingHelp.SlackBot;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParkingFeeDisCountRegistorController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SlackNotifier _slackNotifier;
        private Queue<ParkingDiscountModel> parkingDiscountModels = new Queue<ParkingDiscountModel>();
        public ParkingFeeDisCountRegistorController(AppDbContext context, SlackOptions slackOptions)
        {
            _context = context;
            _slackNotifier = new SlackNotifier(slackOptions);
        }

        [HttpPost()]
        public async Task<IActionResult> PostDiscountParkingFee([FromBody] ParkingDiscountFeePostParam query)
        {
            ParkingDiscountBot.ParkingDiscount parkingDiscount = new ParkingDiscountBot.ParkingDiscount();
            await parkingDiscount.RegisterParkingDiscountAsync(query.CarNumber);
            return BadRequest();
        }
    }
    
}
