using System.Text.Json;
using System.Text.RegularExpressions;
using KanikoRemote.Config;
using Microsoft.Extensions.Logging;

namespace KanikoRemote.Tagger
{
    internal class Tagger
    {
        private readonly ILogger<Tagger> logger;
        private readonly TaggerConfiguration config;
        private readonly IDictionary<Regex, string> regexesByTemplate;
        private bool hasDefault { get => this.config.Default != null; }
        private bool hasStatic { get => this.config.Static != null; }
        private bool hasPrefix { get => this.config.Prefix != null; }
        private bool hasRegex { get => this.regexesByTemplate.Count > 0; }
        public Tagger(TaggerConfiguration config, ILogger<Tagger> logger)
        {
            this.config = config;
            this.logger = logger;

            this.regexesByTemplate = new SortedDictionary<Regex, string>();
            foreach (var (template, replacement) in this.config.Regexes)
            {
                this.regexesByTemplate.Add(new Regex(template), replacement);
            }

            if (this.hasStatic && (this.hasPrefix || this.hasDefault || this.hasRegex))
            {
                throw new InvalidConfigException("No other tags options may be configured when 'static' is set", JsonSerializer.SerializeToNode(this.config)!);
            }
        }

        public IEnumerable<string> TransformTags(IList<string> inputTags)
        {
            if (this.hasStatic)
            {
                this.logger.LogWarning($"Overwriting destination with static tag: {this.config.Static}");
                return new List<string> {this.config.Static!};
            }

            if (inputTags.Count == 0)
            {
                if (!this.hasDefault)
                {
                    throw new InvalidDataException("No tag specified and no default tag configured, specify a tag with -t");
                }
                else
                {
                    this.logger.LogWarning($"Using configured default tag {this.config.Default}");
                    return new List<string> {this.config.Default!};
                }
            }

            // Short circuit to avoid unneccessary logs
            if (!this.hasPrefix && !this.hasRegex)
            {
                return inputTags;
            }

            var outputTags = new List<string>();
            foreach (var tag in inputTags)
            {
                var (regex, replacement) = this.regexesByTemplate.FirstOrDefault(kvp => kvp.Key.IsMatch(tag));
                if (regex != null)
                {
                    outputTags.Add(regex.Replace(tag, replacement));
                }
                else if (this.hasPrefix)
                {
                    outputTags.Add($"{this.config.Prefix}/{tag}");
                }
            }
            var prettyPrint = string.Join(", ", outputTags);
            this.logger.LogInformation($"Adjusted tags to {prettyPrint}");
            return outputTags;
        }
    }
}