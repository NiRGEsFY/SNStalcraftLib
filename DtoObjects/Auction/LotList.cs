using SNStalcraftRequestLib.Objects.Auction;

namespace SNStalcraftRequestLib.DtoObjects.Auction
{
    public class LotList
    {
        public int Total;
        public List<Lot>? Lots { get; set; }
        public List<AuctionItem> ToAuctionItemsList()
        {
            List<AuctionItem> newList = new List<AuctionItem>();
            foreach (var item in Lots)
                newList.Add(item.ToAuctionItem());
            return newList;
        }
    }
}
