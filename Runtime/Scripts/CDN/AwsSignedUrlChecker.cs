using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace AddressableSystem
{
    public static class AwsSignedUrlChecker
    {
        /// <summary>
        /// If you need the exact expiration timestamp (UTC), use this.
        /// Returns null if parsing failed.
        /// </summary>
        public static DateTime? GetUrlExpiryTime(string signedUrl)
        {
            try
            {
                var uri = new Uri(signedUrl);
                var query = uri.Query.TrimStart('?');
                if (string.IsNullOrEmpty(query))
                    return null;

                // Split on “&” into “key=value” pairs
                var parts = query
                    .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

                var dict = parts
                    .Select(kv => kv.Split(new[] { '=' }, 2, StringSplitOptions.None))
                    .Where(pair => pair.Length == 2)
                    .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

                if (!dict.TryGetValue("X-Amz-Date", out string dateStr) ||
                    !dict.TryGetValue("X-Amz-Expires", out string expStr))
                {
                    return null;
                }

                // Parse signing time (UTC)
                if (!DateTime.TryParseExact(
                        dateStr,
                        "yyyyMMdd'T'HHmmss'Z'",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime signingTimeUtc))
                {
                    Debug.LogWarning($"Could not parse X-Amz-Date: {dateStr}");
                    return null;
                }

                // Parse expires (seconds)
                if (!int.TryParse(expStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expiresSeconds))
                {
                    Debug.LogWarning($"Could not parse X-Amz-Expires: {expStr}");
                    return null;
                }

                return signingTimeUtc.AddSeconds(expiresSeconds);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AwsSignedUrlChecker] Exception parsing expiry from '{signedUrl}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns true if the signed URL has not yet expired.
        /// </summary>
        public static bool IsUrlStillValid(string signedUrl)
        {
            var expiry = GetUrlExpiryTime(signedUrl);
            if (!expiry.HasValue)
                return false;

            return DateTime.UtcNow < expiry.Value;
        }
    }
}
