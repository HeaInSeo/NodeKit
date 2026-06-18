using System;

namespace NodeKit.Grpc
{
    internal static class CatalogClientUris
    {
        public static Uri EnsureTrailingSlash(Uri uri)
        {
            var text = uri.ToString();
            return text.EndsWith('/')
                ? uri
                : new Uri(text + "/", UriKind.Absolute);
        }
    }
}
