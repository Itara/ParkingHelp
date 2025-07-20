using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class FavoriteMemberGetParam
    {
        [SwaggerSchema("회원 ID", Format = "int")]
        [DefaultValue(1)]
        public int? MemberId { get; set; }

        [SwaggerSchema("즐겨찾기 회원 ID", Format = "int")]
        [DefaultValue(2)]
        public int? FavoriteMemberId { get; set; }
    }

    public class FavoriteMemberPostParam
    {
        [SwaggerSchema("즐겨찾기 하는 회원 ID", Format = "int")]
        [DefaultValue(1)]
        public int MemberId { get; set; }

        [SwaggerSchema("즐겨찾기 당하는 회원 ID", Format = "int")]
        [DefaultValue(2)]
        public int FavoriteMemberId { get; set; }

        [SwaggerSchema("즐겨찾기 회원 이름", Format = "string")]
        [DefaultValue("이예진")]
        public string FavoriteMemberName { get; set; } = string.Empty;
    }

    public class FavoriteMemberDeleteParam
    {
        [SwaggerSchema("즐겨찾기 ID", Format = "int")]
        [DefaultValue(1)]
        public int Id { get; set; }
    }
}
