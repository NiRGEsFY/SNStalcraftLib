using SNStalcraftRequestLib.Objects.Auction;

namespace SNStalcraftRequestLib.DtoObjects.Auction
{
    public class Lot
    {
        public string ItemId { get; set; }
        public int Amount { get; set; }
        public long StartPrice { get; set; }
        public long CurrentPrice { get; set; }
        public long BuyoutPrice { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public AdditionalItem Additional { get; set; }
        public AuctionItem ToAuctionItem()
        {
            var aucItem = new AuctionItem();
            aucItem.ItemId = ItemId;
            aucItem.StartTime = StartTime;
            aucItem.EndTime = EndTime;
            aucItem.BuyoutPrice = BuyoutPrice;
            aucItem.StartPrice = StartPrice;
            aucItem.CurrentPrice = CurrentPrice;
            aucItem.Ammount = Amount;
            aucItem.Pottential = Additional.Ptn;
            aucItem.Quality = Additional.Qlt;
            aucItem.Stats = Additional.Stats_Random;
            aucItem.State = false;
            return aucItem;
        }
    }
}
