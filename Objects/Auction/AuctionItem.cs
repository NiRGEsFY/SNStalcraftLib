using System.ComponentModel.DataAnnotations;

namespace SNStalcraftRequestLib.Objects.Auction
{
    public partial class AuctionItem
    {
        public long Id { get; set; }
        [MaxLength(20)]
        public string ItemId { get; set; }
        public int? Ammount { get; set; }
        public long? StartPrice { get; set; }
        public long? CurrentPrice { get; set; }
        public long? BuyoutPrice { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? Stats { get; set; }
        public int? Pottential { get; set; }
        public int? Quality { get; set; }
        public bool State { get; set; }
    }
}
