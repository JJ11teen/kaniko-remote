namespace KanikoRemote.Config
{
    internal record TaggerConfiguration : ConfigurableSection
    {
        public string? Default { get; set; }
        public string? Static { get; set; }
        public string? Prefix { get; set; }
        public SortedDictionary<string, string> Regexes { get; set; } = new SortedDictionary<string, string>();
    }
}