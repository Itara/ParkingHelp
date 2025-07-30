using Newtonsoft.Json.Linq;

namespace ParkingHelp.Models
{
    public class ParkingDiscountModel
    {
        public string CarNumber { get; set; } = string.Empty;
        public string MemberEmail { get; set; } = string.Empty;
        public bool IsNotifySlack { get; set; } = false;
        public bool IsGetOffWorkTime = false;
        /// <summary>
        /// 할인권 적용 결과를 담을 JObject 초기에는 Null로 설정
        /// </summary>
        public JObject Result { get; set; } = new JObject();
        public ParkingDiscountModel(string carNumber, string memberEmail, bool isNotifySlack = false ,bool isGetOffWorkTime = true, JObject result = null)
        {
            CarNumber = carNumber;
            MemberEmail = memberEmail;
            if(result != null)
            {
                Result = result;
            }
            IsNotifySlack = isNotifySlack;
            IsGetOffWorkTime = isGetOffWorkTime;
        }
    }
}
