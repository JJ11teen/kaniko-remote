namespace KanikoRemote.Config
{
    internal record TaggerConfiguration : ConfigurableSection
    {
        public string? Default { get; init; }
        public string? Static { get; init; }
        public string? Prefix { get; init; }
        public SortedDictionary<string, string> Regexes { get; init; } = new SortedDictionary<string, string>();
    }
}