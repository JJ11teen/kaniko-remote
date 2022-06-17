namespace KanikoRemote.Tagger
{
    internal class TaggerOptions
    {
        public string? Default { get; set; }
        public string? Static { get; set; }
        public string? Prefix { get; set; }
        public Dictionary<string, string> Regexes { get; set; }

        public TaggerOptions()
        {
            this.Regexes = new Dictionary<string, string>();
        }
    }
}