using Microsoft.Build.ObjectModelRemoting;
using ParkingHelp.Models;

namespace ParkingHelp.DB.QueryCondition
{
    
    public class ParkingDiscountFeePostParam
    {
        public string CarNumber { get; set; } = string.Empty;
        public bool? NotifySlackAlarm { get; set; }
        public List<DiscountTicket>? DisCountList { get; set; } = new List<DiscountTicket>();
    }
}
