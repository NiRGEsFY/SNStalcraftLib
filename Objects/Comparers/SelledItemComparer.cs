using SNStalcraftRequestLib.Objects.Auction;

namespace SNStalcraftRequestLib.Objects.Comparers
{
    public class SelledItemComparer : IEqualityComparer<SelledItem>
    {
        public static readonly SelledItemComparer Instance = new SelledItemComparer();

        public bool Equals(SelledItem x, SelledItem y)
        {
            if(x.Time.Equals(y.Time) && x.Price.Equals(y.Price) && x.Amount.Equals(y.Amount))
                return true;
            return false;
        }
        public int GetHashCode(SelledItem obj)
        {
            return obj.Time.GetHashCode() + obj.Price.GetHashCode() + obj.Amount.GetHashCode();
        }
    }
}
