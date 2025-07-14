using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel;

namespace ParkingHelp.DB.QueryCondition
{
    
    public class RankingGetParam
    {
        [SwaggerSchema("최대 가져올 순위 (기본값 1위부터 50위)", Format = "int")]
        [DefaultValue(50)]
        public int MaxCount { get; set; }
        [SwaggerSchema("정렬 순서 (0: 도와준 횟수 오름차순, 1: 도와준 횟수 내림차순)")]
        [DefaultValue(1)]
        public RankingOrderType OrderType { get; set; } = RankingOrderType.Descending;
        [SwaggerSchema("조회 시작일 (기본값: 이번 달 1일)", Format = "date-time")]
        public DateTime? FromDate { get; set; }
        [SwaggerSchema("조회 종료일 (기본값: 이번 달 말일)", Format = "date-time")]
        public DateTime? ToDate { get; set; } 
    }
}
