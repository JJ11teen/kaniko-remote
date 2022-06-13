namespace KanikoRemote.Auth
{
    internal static class AuthMatcher
    {
        public static IEnumerable<IAuthoriser> GetMatchingAuthorisers(IEnumerable<string> urlsToAuthenticate)
        {
            return new List<IAuthoriser>();
        }
    }
}