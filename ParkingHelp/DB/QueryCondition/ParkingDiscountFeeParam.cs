using Microsoft.Build.ObjectModelRemoting;

namespace ParkingHelp.DB.QueryCondition
{
    
    public class ParkingDiscountFeePostParam
    {
        public string CarNumber { get; set; } = string.Empty;
        public bool? NotifySlackAlarm { get; set; }
    }
}
