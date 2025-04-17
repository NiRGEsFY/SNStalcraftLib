using SNStalcraftRequestLib.Objects.Auction;

namespace SNStalcraftRequestLib.DtoObjects.Auction
{
    public class Prices
    {
        public int Amount { get; set; }
        public long Price { get; set; }
        public DateTime Time { get; set; }
        public AdditionalItem Additional { get; set; }
        public override string ToString()
        {
            return $"Цена: {Price}\n" +
                   $"Время покупки: {Time}";
        }
        public SelledItem ToSelledItem(string itemId)
        {
            var selledItem = new SelledItem();
            selledItem.Amount = Amount;
            selledItem.Price = Price;
            selledItem.Time = Time;
            selledItem.ItemId = itemId;
            selledItem.Pottential = Additional.Ptn;
            selledItem.Quality = Additional.Qlt;
            selledItem.Stats = Additional.Stats_Random;
            return selledItem;
        }
    }
}
