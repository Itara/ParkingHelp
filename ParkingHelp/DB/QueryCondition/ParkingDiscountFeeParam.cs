using Microsoft.Build.ObjectModelRemoting;
using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json.Serialization;

namespace ParkingHelp.DB.QueryCondition
{
    

    public class ParkingDiscountFeePostParam
    {
        /// <summary>차량번호 (예: 10저3519)</summary>
        [SwaggerSchema("차량번호", Format = "string")]
        public string CarNumber { get; set; } = string.Empty;
        /// <summary>유료할인권 리스트 해당 파라미터 없으면 기본할인권만 적용</summary>
        [SwaggerSchema("유료할인권 리스트 해당 파라미터 없으면 기본할인권만 적용", Format = "array")]
        public List<DiscountTicket>? DisCountList { get; set; } = new List<DiscountTicket>();
    }
}
