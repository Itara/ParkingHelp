using ParkingHelp.DB.QueryCondition;
using ParkingHelp.Models;
using Swashbuckle.AspNetCore.Filters;

public class ParkingDiscountFeePostParamExample : IExamplesProvider<ParkingDiscountFeePostParam>
{
    public ParkingDiscountFeePostParam GetExamples()
    {
        return new ParkingDiscountFeePostParam
        {
            CarNumber = "10저3519",
            DisCountList = new List<DiscountTicket>
            {
                DiscountTicket.Min30,
                DiscountTicket.Hour1,
                DiscountTicket.Hour4
            }
        };
    }
}