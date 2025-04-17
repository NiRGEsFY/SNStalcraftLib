namespace SNStalcraftRequestLib.DtoObjects.Clan
{
    public class Info
    {
        public string id { get; set; }
        public string name { get; set; }
        public string tag { get; set; }
        public int level { get; set; }
        public int levelPoints { get; set; }
        public DateTime registrationTime { get; set; }
        public string alliance { get; set; }
        public string description { get; set; }
        public string leader { get; set; }
        public int memberCount { get; set; }
    }
}
