using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShovelTools;   // for DLM

namespace AddressableSystem
{
    /// <summary>
    /// Fired once on Unity’s main thread when a signed URL (identified by its 'key') expires.
    /// </summary>
    public static class UrlExpiryManager
    {
        public static event Action<string> OnUrlExpired;
        private static SynchronizationContext _syncContext;
        private static HashSet<string> _registeredKeys = new HashSet<string>();
        private static readonly DLM.FeatureFlags _featureFlag = DLM.FeatureFlags.Addressables;

        /// <summary>
        /// Call this exactly once from your main‐thread startup.
        /// </summary>
        public static void Initialize()
        {
            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
                DLM.Log(_featureFlag, "[UrlExpiryManager] Main-thread context captured");
            }
        }

        /// <summary>
        /// Schedule a fire at expiryUtc (or immediately if already passed).
        /// Logs exactly once per key.
        /// </summary>
        public static void Register(string key, DateTime expiryUtc)
        {
            if (!_registeredKeys.Add(key))
            {
                // Already scheduled
                return;
            }

            var now = DateTime.UtcNow;
            var delay = expiryUtc - now;
            DLM.Log(_featureFlag,
                $"[UrlExpiryManager] Registering '{key}' → expires at {expiryUtc:O} (in {delay.TotalSeconds:F0}s)");

            if (delay <= TimeSpan.Zero)
            {
                DLM.LogWarning(_featureFlag,
                    $"[UrlExpiryManager] '{key}' already expired (expiryUtc={expiryUtc:O}), triggering immediately");
                Trigger(key);
            }
            else
            {
                ScheduleExpiry(key, delay);
            }
        }

        private static async void ScheduleExpiry(string key, TimeSpan delay)
        {
            try
            {
                DLM.Log(_featureFlag,
                    $"[UrlExpiryManager] Scheduling expiry for '{key}' in {delay.TotalSeconds:F0}s");
                await Task.Delay(delay).ConfigureAwait(false);
                Trigger(key);
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag,
                    $"[UrlExpiryManager] Scheduling failed for '{key}': {ex}");
            }
        }

        private static void Trigger(string key)
        {
            DLM.Log(_featureFlag, $"[UrlExpiryManager] Triggering OnUrlExpired for '{key}'");

            void Fire()
            {
                OnUrlExpired?.Invoke(key);
                _registeredKeys.Remove(key);
                DLM.Log(_featureFlag,
                    $"[UrlExpiryManager] Completed expiry handling for '{key}'");
            }

            if (_syncContext != null)
            {
                _syncContext.Post(_ => Fire(), null);
            }
            else
            {
                Fire();
            }
        }
    }
}
