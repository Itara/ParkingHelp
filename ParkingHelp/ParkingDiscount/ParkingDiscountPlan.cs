namespace ParkingHelp.ParkingDiscount
{
    public class DiscountInventory
    {
        public int Count30Min { get; set; }
        public int Count1Hour { get; set; }
        public int Count4Hour { get; set; }
    }
    public class ParkingDiscountPlan
    {
        public int Use30Min { get; set; }
        public int Use1Hour { get; set; }
        public int Use4Hour { get; set; }

        public int UncoveredMinutes { get; set; } // 미처리된 시간 (할인권 부족 시)

        public int MinutesUntilNextFee { get; set; } // 다음 요금까지 남은 시간

        public override string ToString()
        {
            return $"4시간권: {Use4Hour}개, 1시간권: {Use1Hour}개, 30분권: {Use30Min}개"
                 + (UncoveredMinutes > 0 ? $" (할인권 부족: {UncoveredMinutes}분 미처리)" : "");
        }
    }
}
