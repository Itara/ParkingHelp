//using Swashbuckle.AspNetCore.Annotations;
using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    public class MemberAddParam
    {
        [SwaggerSchema("로그인Id", Format = "string")]
        [DefaultValue("1054")]
        public string memberLoginId { get; set; } = string.Empty; // 회원 ID
       [SwaggerSchema("사용자명", Format = "string")]
        [DefaultValue("박주현")]
        public string memberName { get; set; } = string.Empty; // 회원명
        [SwaggerSchema("사용자 차량번호", Format = "string")]
        [DefaultValue("10저3519")]
        public string carNumber { get; set; } = string.Empty; // 차량 번호

        [SwaggerSchema("Slack에 연동될 Email", Format = "string")]
        [DefaultValue("@pharmsoft.co.kr")]
        public string? email { get; set; } = string.Empty; // Email
    }

    public class MemberGetParam
    {
       [SwaggerSchema("로그인Id", Format = "string")]
        [DefaultValue("1054")]
        public string memberLoginId { get; set; } = string.Empty; // 회원 ID
       [SwaggerSchema("사용자명", Format = "string")]
        [DefaultValue("박주현")]
        public string memberName { get; set; } = string.Empty; // 회원명
        [SwaggerSchema("사용자 차량번호", Format = "string")]
        [DefaultValue("10저3519")]
        public string carNumber { get; set; } = string.Empty; // 차량 번호

        public CarHelpStatus? Status { get; set; } // 차량 번호
    }

    public class MemberUpdateParam
    {
        //[SwaggerSchema("로그인Id", Format = "string")]
        //[DefaultValue("1054")]
        //public string memberId { get; set; } = string.Empty; // 회원 ID

        //[SwaggerSchema("사용자명", Format = "string")]
        //[DefaultValue("박주현")]
        //public string password { get; set; } = string.Empty; // 회원 비번

        [SwaggerSchema("사용자 차량번호", Format = "string")]
        [DefaultValue("10저3519")]
        public string carNumber { get; set; } = string.Empty; // 차량 번호
        
    }

    public class  MemberDeleteParam
    {
        [SwaggerSchema("로그인Id", Format = "string")]
        [DefaultValue("1054")]
        public string memberId { get; set; } = string.Empty; // 회원 ID
    }
}
