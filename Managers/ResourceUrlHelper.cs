namespace SubscriptionProxy.Managers
{
    static public class UrlHelper
    {
        public static string ForResourceId(string resource, string id)
        {
            if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return new Uri(
                new Uri(Program.Configuration["Server_Public_Url"], UriKind.Absolute),
                new Uri($"/{resource}/{id}", UriKind.Relative))
                .ToString();
        }
    }
}
