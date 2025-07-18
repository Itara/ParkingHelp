using ParkingHelp.Models;

namespace ParkingHelp.DB.QueryCondition
{
    public class RequestHelpDetailPutParam
    {
        public DateTimeOffset? DisCountApplyDate { get; set; }
        public DiscountApplyType? DisCountApplyType { get; set; }
        public int? HelperMemId { get; set; }
    }
}
