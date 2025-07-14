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
    public class RequestHelpController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RequestHelpController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 주차등록 요청 조회
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet()]
        public async Task<IActionResult> GetRequestHelp([FromQuery] RequestHelpGetParam query)
        {
            try
            {
                DateTime nowKST = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"));
                DateTime startOfToday = nowKST.Date; //
                DateTime endOfToday = startOfToday.AddDays(1).AddSeconds(-1);
                
                DateTime fromDate = query.FromReqDate ?? startOfToday;
                DateTime toDate = query.ToReqDate ?? endOfToday;

                var reqHelpsQuery = _context.ReqHelps
                    .Include(r => r.HelpRequester)
                    .Include(r => r.Helper)
                    .Include(r => r.ReqCar)
                    .Where(x => x.ReqDate >= fromDate && x.ReqDate <= toDate);

                if (query.HelpReqMemId.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.HelpRequester.Id == query.HelpReqMemId);
                }

                if (query.HelperMemId.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.Helper != null && r.Helper.Id == query.HelperMemId);
                }

                if (!string.IsNullOrEmpty(query.ReqCarNumber))
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.ReqCar != null && r.ReqCar.CarNumber == query.ReqCarNumber);
                }

                if (query.Status.HasValue)
                {
                    reqHelpsQuery = reqHelpsQuery.Where(r => r.Status == query.Status);
                }

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

        [HttpPost()]
        public async Task<IActionResult> PostRequestHelp([FromBody] RequestHelpPostParam query)
        {
            try
            {
                var newReqHelp = new ReqHelp
                {
                    HelpReqMemId = query.HelpReqMemId ?? 0,
                    Status = 0,
                    ReqCarId = query.CarId,
                    ReqDate = DateTime.UtcNow
                };
                _context.ReqHelps.Add(newReqHelp);
                await _context.SaveChangesAsync();

                var returnNewReqHelps = await _context.ReqHelps
                   .Include(r => r.HelpRequester)
                   .Include(r => r.Helper)
                   .Include(r => r.ReqCar)
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
                   .Where(x => x.Id == newReqHelp.Id)
                   .ToListAsync();


                return Ok(returnNewReqHelps);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }

        [HttpPut("{id}")]
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

                var updateReqHelps = await _context.ReqHelps
                   .Include(r => r.HelpRequester)
                   .Include(r => r.Helper)
                   .Include(r => r.ReqCar)
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
                    }).Where(x => x.Id == id)
                   .ToListAsync();


                return Ok(updateReqHelps);
            }
            catch (Exception ex)
            {
                JObject jResult = GetErrorJobject(ex.Message, ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }
        [HttpDelete("{id}")]
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
                JObject jResult = GetErrorJobject(ex.Message,ex.InnerException?.ToString() ?? "InnerException is Null");
                return BadRequest(jResult.ToString());
            }
        }


        private JObject GetErrorJobject(string errorMessage,string InnerExceptionMessage)
        {
            return new JObject
            {
                { "Result", "Error" },
                { "ErrorMsg", errorMessage },
                { "InnerException" , InnerExceptionMessage}
            };
        }
    }
}
