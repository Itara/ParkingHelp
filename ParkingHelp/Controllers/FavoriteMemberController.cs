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
    public class FavoriteMemberController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FavoriteMemberController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/FavoriteMember
        [HttpGet]
        public async Task<IActionResult> GetFavoriteMembers([FromQuery] FavoriteMemberGetParam param)
        {
            try
            {
                var query = _context.FavoriteMembers
                    .Where(f => f.DelYn == "N")
                    .AsQueryable();

                // 조건이 있는 것만 차례대로 붙임
                if (param.MemberId.HasValue)
                {
                    query = query.Where(f => f.MemberId == param.MemberId.Value);
                }

                if (param.FavoriteMemberId.HasValue)
                {
                    query = query.Where(f => f.FavoriteMemberId == param.FavoriteMemberId.Value);
                }

                var result = await query
                    .OrderByDescending(f => f.CreateDate)
                    .ToListAsync();

                var favoriteMemberDtos = result.Select(f => new FavoriteMemberListDTO
                {
                    Id = f.Id,
                    FavoriteMemberId = f.FavoriteMemberId,
                    FavoriteMemberName = f.FavoriteMemberName,
                    CreateDate = f.CreateDate
                }).ToList();

                return Ok(favoriteMemberDtos);
            }
            catch (Exception ex)
            {
                var result = new JObject
                {
                    { "Result", "Fail" },
                    { "ErrMsg", ex.Message }
                };
                return BadRequest(result.ToString());
            }
        }

        // POST: api/FavoriteMember
        [HttpPost]
        public async Task<IActionResult> AddFavoriteMember([FromBody] FavoriteMemberPostParam param)
        {
            try
            {
                // 이미 즐겨찾기 되어 있는지 확인
                var existingFavorite = await _context.FavoriteMembers
                    .FirstOrDefaultAsync(f => f.MemberId == param.MemberId 
                                            && f.FavoriteMemberId == param.FavoriteMemberId 
                                            && f.DelYn == "N");

                if (existingFavorite != null)
                {
                    var result = new JObject
                    {
                        { "Result", "Fail" },
                        { "ErrMsg", "이미 즐겨찾기된 회원입니다." }
                    };
                    return BadRequest(result.ToString());
                }

                var newFavorite = new FavoriteMemberModel
                {
                    MemberId = param.MemberId,
                    FavoriteMemberId = param.FavoriteMemberId,
                    FavoriteMemberName = param.FavoriteMemberName.Trim(),
                    DelYn = "N",
                    CreateDate = DateTimeOffset.UtcNow,
                    UpdateDate = DateTimeOffset.UtcNow
                };

                await _context.FavoriteMembers.AddAsync(newFavorite);
                await _context.SaveChangesAsync();

                var favoriteMemberDto = new FavoriteMemberDTO
                {
                    Id = newFavorite.Id,
                    MemberId = newFavorite.MemberId,
                    FavoriteMemberId = newFavorite.FavoriteMemberId,
                    FavoriteMemberName = newFavorite.FavoriteMemberName,
                    CreateDate = newFavorite.CreateDate,
                    UpdateDate = newFavorite.UpdateDate
                };

                return Ok(favoriteMemberDto);
            }
            catch (Exception ex)
            {
                var result = new JObject
                {
                    { "Result", "Fail" },
                    { "ErrMsg", ex.Message }
                };
                return BadRequest(result.ToString());
            }
        }

        // DELETE: api/FavoriteMember/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteFavoriteMember(int id)
        {
            try
            {
                var favoriteMember = await _context.FavoriteMembers
                    .FirstOrDefaultAsync(f => f.Id == id && f.DelYn == "N");

                if (favoriteMember == null)
                {
                    var result = new JObject
                    {
                        { "Result", "Fail" },
                        { "ErrMsg", "즐겨찾기를 찾을 수 없습니다." }
                    };
                    return NotFound(result.ToString());
                }

                // Soft Delete
                favoriteMember.DelYn = "Y";
                favoriteMember.UpdateDate = DateTimeOffset.UtcNow;
                
                _context.FavoriteMembers.Update(favoriteMember);
                await _context.SaveChangesAsync();

                var deleteResult = new JObject
                {
                    { "Result", "Success" },
                    { "Message", "즐겨찾기가 삭제되었습니다." }
                };

                return Ok(deleteResult.ToString());
            }
            catch (Exception ex)
            {
                var result = new JObject
                {
                    { "Result", "Fail" },
                    { "ErrMsg", ex.Message }
                };
                return BadRequest(result.ToString());
            }
        }
    }
} 