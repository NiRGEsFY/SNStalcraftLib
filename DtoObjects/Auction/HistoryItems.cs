using SNStalcraftRequestLib.Objects.Auction;

namespace SNStalcraftRequestLib.DtoObjects.Auction
{
    public class HistoryItems
    {
        public string ItemId { get; set; }
        public long Total { get; set; }
        public List<Prices> Prices { get; set; }
        public List<SelledItem> ToSelledItemList()
        {
            var newList = new List<SelledItem>();
            foreach (var item in Prices)
                newList.Add(item.ToSelledItem(ItemId));
            return newList;
        }
    }
}