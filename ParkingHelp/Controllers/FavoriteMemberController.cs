using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkingHelp.DB;
using ParkingHelp.DTO;
using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;

namespace ParkingHelp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FavoriteMemberController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FavoriteMemberController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 즐겨찾기 멤버 목록 조회
        /// </summary>
        /// <param name="param">조회 파라미터 (MemberId만 필요)</param>
        /// <returns>즐겨찾기 멤버 목록 (ID, 이름, 차번호)</returns>
        /// <response code="200">즐겨찾기 목록 조회 성공</response>
        /// <response code="404">멤버가 존재하지 않거나 즐겨찾기 목록이 없음</response>
        /// <response code="400">잘못된 요청</response>
        [HttpGet]
        public async Task<IActionResult> GetFavoriteMembers([FromQuery] FavoriteMemberGetParam param)
        {
            try
            {
                // 1. 멤버가 존재하는지 먼저 확인 (PK로 비교)
                var member = await _context.Members.FirstOrDefaultAsync(m => m.Id == param.MemberId);
                if (member == null)
                {
                    return NotFound(new { message = $"존재하지 않는 멤버입니다. (ID: {param.MemberId})" });
                }

                // 2. 즐겨찾기 테이블에서 해당 멤버의 즐겨찾기 목록 조회 (삭제되지 않은 것만)
                var query = _context.FavoriteMembers
                    .Include(f => f.FavoriteMember) // Member 테이블 조인
                    .ThenInclude(m => m.Cars) // Member의 Cars 테이블도 조인
                    .Where(f => f.MemberId == param.MemberId && f.DelYn == "N");

                var result = await query.ToListAsync();

                // 3. 즐겨찾기 목록이 없으면 404 반환
                if (!result.Any())
                {
                    return NotFound(new { message = "즐겨찾기 목록이 없습니다." });
                }

                // 3. 응답 DTO로 변환 (ID, 이름, 차번호만 포함)
                var favoriteMemberDtos = result.Select(f => new FavoriteMemberListDTO
                {
                    FavoriteMemberId = f.FavoriteMemberId, // 즐겨찾기한 멤버 ID
                    FavoriteMemberName = f.FavoriteMember.MemberName, // 실시간으로 Member 테이블에서 이름 가져옴
                    CarNumber = f.FavoriteMember.Cars.FirstOrDefault()?.CarNumber ?? "" // 첫 번째 차번호 (없으면 빈 문자열)
                }).ToList();

                return Ok(favoriteMemberDtos);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// 즐겨찾기 멤버 동기화 (전체 리스트 교체)
        /// </summary>
        /// <param name="id">현재 멤버 ID (URL 경로에서 가져옴)</param>
        /// <param name="param">새로운 즐겨찾기 멤버 ID 목록</param>
        /// <returns>동기화 결과 (추가된 개수, 삭제된 개수, 추가된 멤버 목록)</returns>
        /// <response code="200">즐겨찾기 동기화 성공</response>
        /// <response code="400">잘못된 요청 (존재하지 않는 멤버, 차량번호가 없는 멤버 등)</response>
        /// <response code="404">멤버가 존재하지 않음</response>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateFavoriteMembers(int id, [FromBody] FavoriteMemberPutParam param)
        {
            try
            {
                // 1. 현재 멤버가 존재하는지 확인 (PK로 비교)
                var member = await _context.Members.FirstOrDefaultAsync(m => m.Id == id);
                if (member == null)
                {
                    return NotFound(new { message = $"존재하지 않는 멤버입니다." });
                }

                // 2. 트랜잭션 시작 (데이터 일관성 보장)
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // 3. 현재 즐겨찾기 목록 조회 (삭제되지 않은 것만)
                    var currentFavorites = await _context.FavoriteMembers
                        .Where(f => f.MemberId == id && f.DelYn == "N")
                        .ToListAsync();

                    // 4. 새로 추가할 즐겨찾기 ID 목록 (현재 목록에 없는 것들 + 기존에 삭제된 것들 제외)
                    var existingDeletedFavorites = await _context.FavoriteMembers
                        .Where(f => f.MemberId == id && f.DelYn == "Y" && param.FavoriteMemberIds.Contains(f.FavoriteMemberId))
                        .ToListAsync();
                        
                    var favoritesToAdd = param.FavoriteMemberIds
                        .Where(newId => !currentFavorites.Any(cf => cf.FavoriteMemberId == newId) && 
                                       !existingDeletedFavorites.Any(edf => edf.FavoriteMemberId == newId))
                        .ToList();

                    // 5. 삭제할 즐겨찾기 ID 목록 (새 목록에 없는 현재 것들)
                    var favoritesToDelete = currentFavorites
                        .Where(cf => !param.FavoriteMemberIds.Contains(cf.FavoriteMemberId))
                        .ToList();


                    // 7. 기존 즐겨찾기들을 soft delete (del_yn = "Y")
                    foreach (var favorite in favoritesToDelete)
                    {
                        favorite.DelYn = "Y";
                        favorite.UpdateDate = DateTimeOffset.UtcNow;
                        _context.FavoriteMembers.Update(favorite);
                    }

                    // 8. 기존에 삭제된 즐겨찾기들을 다시 활성화 (del_yn = "N")
                    foreach (var favorite in existingDeletedFavorites)
                    {
                        favorite.DelYn = "N";
                        favorite.UpdateDate = DateTimeOffset.UtcNow;
                        _context.FavoriteMembers.Update(favorite);
                    }

                    // 9. 새로운 즐겨찾기들 추가 (기존에 없던 것들만)
                    var addedFavorites = new List<FavoriteMemberDTO>();
                    
                    foreach (int favoriteMemberId in favoritesToAdd)
                    {
                        // 추가하려는 멤버가 실제로 존재하는지 확인
                        var favoriteMember = await _context.Members
                            .Include(m => m.Cars) // 차 정보도 함께 가져옴
                            .FirstOrDefaultAsync(m => m.Id == favoriteMemberId);
                        
                        if (favoriteMember == null)
                        {
                            return BadRequest(new { error = $"존재하지 않는 멤버입니다." });
                        }
                        
                        // 차량번호가 있는지 확인 (더 엄격한 검증)
                        var validCars = favoriteMember.Cars.Where(c => !string.IsNullOrEmpty(c.CarNumber) && !string.IsNullOrWhiteSpace(c.CarNumber)).ToList();
                        var hasCar = validCars.Any();
                        
                        if (!hasCar)
                        {
                            return BadRequest(new { error = $"차량번호가 없는 멤버는 즐겨찾기에 추가할 수 없습니다. ({favoriteMember.MemberName})" });
                        }
                        
                        // 새로운 즐겨찾기 레코드 생성
                        var newFavorite = new FavoriteMemberModel
                        {
                            MemberId = id, // 현재 멤버 ID
                            FavoriteMemberId = favoriteMemberId, // 즐겨찾기할 멤버 ID
                            DelYn = "N", // 활성 상태
                            CreateDate = DateTimeOffset.UtcNow,
                            UpdateDate = DateTimeOffset.UtcNow
                        };
                        
                        await _context.FavoriteMembers.AddAsync(newFavorite);
                        
                        // 응답용 DTO 생성 (내부 사용)
                        addedFavorites.Add(new FavoriteMemberDTO
                        {
                            Id = newFavorite.Id,
                            MemberId = newFavorite.MemberId,
                            FavoriteMemberId = newFavorite.FavoriteMemberId,
                            FavoriteMemberName = favoriteMember.MemberName, // 실시간으로 이름 가져옴
                            DelYn = newFavorite.DelYn
                        });
                    }

                    // 10. 데이터베이스에 변경사항 저장
                    await _context.SaveChangesAsync();
                    
                    // 11. 트랜잭션 커밋
                    await transaction.CommitAsync();

                    // 12. 성공 응답 반환
                    return Ok(new
                    {
                        Result = "Success",
                        Message = "즐겨찾기가 성공적으로 업데이트되었습니다.",
                        AddedCount = addedFavorites.Count, // 새로 추가된 개수
                        DeletedCount = favoritesToDelete.Count, // 삭제된 개수
                        RestoredCount = existingDeletedFavorites.Count, // 복원된 개수
                        AddedFavorites = addedFavorites.Select(f => new // 응답용 데이터 (ID, 이름, 차번호만)
                        {
                            FavoriteMemberId = f.FavoriteMemberId,
                            FavoriteMemberName = f.FavoriteMemberName,
                            CarNumber = _context.Members // 차번호는 다시 조회 (최신 정보 보장)
                                .Include(m => m.Cars)
                                .Where(m => m.Id == f.FavoriteMemberId)
                                .SelectMany(m => m.Cars)
                                .Select(c => c.CarNumber)
                                .FirstOrDefault() ?? ""
                        }).ToList()
                    });
                }
                catch (Exception ex)
                {
                    // 11. 오류 발생 시 트랜잭션 롤백
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}