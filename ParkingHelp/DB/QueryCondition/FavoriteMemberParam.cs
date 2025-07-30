using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;

namespace ParkingHelp.DB.QueryCondition
{
    public class FavoriteMemberGetParam
    {
        [SwaggerSchema("회원 ID", Format = "integer")]
        public int? MemberId { get; set; }
    }

    public class FavoriteMemberPutParam
    {
        [SwaggerSchema("즐겨찾기할 회원들 ID 목록", Format = "array")]
        public List<int> FavoriteMemberIds { get; set; } = null!;
    }
}
