using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkingHelp.DB;
using ParkingHelp.DB.DTO;
using ParkingHelp.DB.QueryCondition;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ParkingHelp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelpHistoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public HelpHistoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet()]
        public async Task<IActionResult> GetHelpHistory([FromQuery] HelpHistoryGetParam param)
        {
            try
            {
                DateTime startOfToday = DateTime.UtcNow.Date; // 현재 날짜의 시작
                DateTime endOfToday = startOfToday.AddDays(1).AddSeconds(-1); // 현재 날짜의 끝
                DateTime from = param.FromHelpDate ?? startOfToday;
                DateTime to = param.ToHelpDate ?? endOfToday;
                List<ReqHelpDto> helpListFromReqHelp = await GetHelpListFromReqHelp(param.HelperMemId, from,to);
                List<HelpOfferDTO> helpListFromHelpOffer = await GetHelpListFromHelpOffer(param.HelperMemId, from, to);
                HelpHistoryDTO helpHistoryDTO = new HelpHistoryDTO
                {
                    ReqHelps = helpListFromReqHelp,
                    HelpOffers = helpListFromHelpOffer
                };
                helpHistoryDTO.HelperMemId = param.HelperMemId;
                helpHistoryDTO.TotalCount = helpListFromReqHelp.Count + helpListFromHelpOffer.Count;
                return Ok(helpHistoryDTO);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Result = "Fail", ErrMsg = ex.Message });
            }
        }
        [NonAction]
        public async Task<List<ReqHelpDto>> GetHelpListFromReqHelp(int helpMemberId, DateTime fromDate, DateTime toDate)
        {
            var reqHelpsQuery = _context.ReqHelps
                .Include(r => r.HelpRequester)
                .Include(r => r.Helper)
                .Include(r => r.ReqCar)
                .Where(x => x.HelpDate >= fromDate && x.HelpDate <= toDate);

            reqHelpsQuery = reqHelpsQuery.Where(r => r.Helper != null && r.Helper.Id == helpMemberId && r.Status == Models.CarHelpStatus.Completed);
            var reqHelps = await reqHelpsQuery
            .Select(r => new ReqHelpDto
            {
                Id = r.Id,
                ReqDate = r.ReqDate,
                HelpDate = r.HelpDate,
                Status = r.Status,
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
            return reqHelps;
        }
        [NonAction]
        public async Task<List<HelpOfferDTO>> GetHelpListFromHelpOffer(int helpMemberId, DateTime fromDate, DateTime toDate)
        {
            var helpOfferQuery = _context.HelpOffers
                .Include(r => r.Requester)
                .Include(r => r.Helper)
                .Include(r => r.ReserveCar)
                .Where(x => x.HelpDate >= fromDate && x.HelpDate <= toDate && x.Status == Models.CarHelpStatus.Completed && x.Helper.Id == helpMemberId);

            var reqHelps = await helpOfferQuery
            .Select(r => new HelpOfferDTO
            {
                Id = r.Id,
                HelpDate = r.HelpDate,
                RequestDate = r.ReqDate,
                Status = r.Status,
                Helper = r.Helper == null ? null : new HelperDto
                {
                    Id = r.Helper.Id,
                    HelperName = r.Helper.MemberName
                },
                HelpRequester = new HelpRequesterDto
                {
                    Id = r.Requester!.Id,
                    HelpRequesterName = r.Requester.MemberName
                },
                ReqCar = r.ReserveCar == null ? null : new HelpReserveCarDto
                {
                    Id = r.ReserveCar.Id,
                    CarNumber = r.ReserveCar.CarNumber
                }
            })
            .ToListAsync();
            return reqHelps;
        }
    }
}
