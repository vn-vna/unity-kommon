#if CHRONO_NTP

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Com.Hapiga.Scheherazade.Common.Logging;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(fileName = "NtpTimeProvider", menuName = "Scheherazade/Chrono/NTP Time Provider")]
    public class NtpTimeProvider : TimeProviderBase, ITickableTimeProvider
    {
        private const int DefaultTimeoutMs = 3000;
        private const float DefaultSyncIntervalSeconds = 3600f;

        private static readonly string[] DefaultNtpHosts =
        {
            "time.google.com",
            "time.cloudflare.com",
            "time.windows.com",
            "pool.ntp.org"
        };

        [SerializeField] private string[] _ntpHosts = DefaultNtpHosts;

        [SerializeField] private int _timeoutMs = DefaultTimeoutMs;

        [SerializeField] private float _syncIntervalSeconds = DefaultSyncIntervalSeconds;

        [NonSerialized] private DateTime _lastSyncTime;

        [NonSerialized] private Stopwatch _stopwatch;

        [NonSerialized] private bool _hasSync;

        [NonSerialized] private bool _syncInProgress;

        private void OnEnable()
        {
            _lastSyncTime = DateTime.MinValue;
            _stopwatch = new Stopwatch();
            _hasSync = false;
            _syncInProgress = false;
        }

        private void OnDisable()
        {
            _stopwatch?.Stop();
        }

        public override DateTime Now
        {
            get
            {
                return _hasSync
                    ? _lastSyncTime.Add(_stopwatch.Elapsed).ToLocalTime()
                    : DateTime.Now;
            }
        }

        public override DateTime UtcNow
        {
            get
            {
                return _hasSync
                    ? _lastSyncTime.Add(_stopwatch.Elapsed)
                    : DateTime.UtcNow;
            }
        }

        public override DateTime Today => Now.Date;

        public override DateTime Epoch => DateTime.UnixEpoch;

        public void Tick(float deltaTime)
        {
            if (_syncInProgress)
            {
                return;
            }

            if (!_hasSync
                || _stopwatch.Elapsed.TotalSeconds >= _syncIntervalSeconds)
            {
                Sync();
            }
        }

        public async void Sync()
        {
            if (_syncInProgress)
            {
                return;
            }

            _syncInProgress = true;

            try
            {
                DateTime? result = await SyncFromAllHostsAsync();
                if (!result.HasValue)
                {
                    return;
                }

                _lastSyncTime = result.Value;
                _stopwatch.Restart();
                _hasSync = true;
                QuickLog.Debug<NtpTimeProvider>("Sync succeed: {0}", _lastSyncTime);
            }
            catch (Exception ex)
            {
                QuickLog.Critical<NtpTimeProvider>("Sync failed: {0}", ex);
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        public void SyncBlocking()
        {
            if (_syncInProgress)
            {
                return;
            }

            _syncInProgress = true;

            try
            {
                DateTime? result = SyncFromAllHosts();
                if (!result.HasValue) return;
                _lastSyncTime = result.Value;
                _stopwatch.Restart();
                _hasSync = true;

                QuickLog.Info<NtpTimeProvider>(
                    "NTP pulse update sucessfully with synced time: [{0}]",
                    _lastSyncTime.ToUniversalTime()
                );
            }
            catch (Exception ex)
            {
                QuickLog.Critical<NtpTimeProvider>(
                    "Sync failed due to error: {0}",
                    ex.Message
                );
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        private async Task<DateTime?> SyncFromAllHostsAsync()
        {
            if (_ntpHosts == null || _ntpHosts.Length == 0)
            {
                return null;
            }

            var tasks = new Task<DateTime>[_ntpHosts.Length];
            for (int i = 0; i < _ntpHosts.Length; i++)
            {
                string host = _ntpHosts[i];
                tasks[i] = NtpClient.GetNetworkTimeAsync(host, _timeoutMs);
            }

            var results = new System.Collections.Generic.List<DateTime>();

            for (int i = 0; i < tasks.Length; i++)
            {
                try
                {
                    DateTime time = await tasks[i];
                    results.Add(time);
                }
                catch
                {
                    // Server timed out or failed — skip
                }
            }

            return results.Count > 0
                ? AverageDateTime(results)
                : (DateTime?)null;
        }

        private DateTime? SyncFromAllHosts()
        {
            if (_ntpHosts == null || _ntpHosts.Length == 0)
            {
                return null;
            }

            var results = new System.Collections.Generic.List<DateTime>();

            for (int i = 0; i < _ntpHosts.Length; i++)
            {
                try
                {
                    DateTime time = NtpClient.GetNetworkTime(
                        _ntpHosts[i],
                        _timeoutMs
                    );

                    results.Add(time);
                }
                catch
                {
                    // Server timed out or failed — skip
                }
            }

            return results.Count > 0
                ? AverageDateTime(results)
                : (DateTime?)null;
        }

        internal static DateTime AverageDateTime(
            System.Collections.Generic.IReadOnlyList<DateTime> times)
        {
            if (times == null || times.Count == 0)
            {
                throw new ArgumentException("At least one time is required.");
            }

            if (times.Count == 1)
            {
                return times[0];
            }

            decimal totalTicks = 0;
            for (int i = 0; i < times.Count; i++)
            {
                totalTicks += times[i].Ticks;
            }

            long averageTicks = (long)(totalTicks / times.Count);

            return new DateTime(averageTicks, DateTimeKind.Utc);
        }

        public DateTime? LastSyncTime => _hasSync ? _lastSyncTime : (DateTime?)null;

        public TimeSpan Age => _hasSync
            ? _stopwatch.Elapsed
            : TimeSpan.Zero;
    }

    public static class NtpClient
    {
        private static readonly DateTime NtpEpoch = new DateTime(
            1900, 1, 1, 0, 0, 0, DateTimeKind.Utc
        );

        public static DateTime GetNetworkTime(string host, int timeout = 3000)
        {
            var ipAddresses = Dns.GetHostAddresses(host);
            if (ipAddresses == null || ipAddresses.Length == 0)
                throw new ArgumentException("Could not resolve host.", nameof(host));

            var endPoint = new IPEndPoint(ipAddresses[0], 123);
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeout;

            var request = new byte[48];
            request[0] = 0x1B;

            DateTime t0 = DateTime.UtcNow;
            WriteTimestamp(request, 40, t0);

            udp.Connect(endPoint);
            udp.Send(request, request.Length);

            var responseEP = new IPEndPoint(IPAddress.Any, 0);
            var response = udp.Receive(ref responseEP);
            DateTime t3 = DateTime.UtcNow;

            if (response == null || response.Length < 48)
                throw new InvalidOperationException("Invalid NTP response.");

            DateTime originate = ReadTimestamp(response, 24);
            DateTime receive = ReadTimestamp(response, 32);
            DateTime transmit = ReadTimestamp(response, 40);

            double offsetMs = (
                (receive - t0).TotalMilliseconds + (transmit - t3).TotalMilliseconds
            ) / 2.0;

            DateTime corrected = t3.AddMilliseconds(offsetMs);
            return DateTime.SpecifyKind(corrected, DateTimeKind.Utc);
        }

        public static async Task<DateTime> GetNetworkTimeAsync(string host, int timeout = 3000)
        {
            var ipAddresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (ipAddresses == null || ipAddresses.Length == 0)
                throw new ArgumentException("Could not resolve host.", nameof(host));

            var endPoint = new IPEndPoint(ipAddresses[0], 123);
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeout;
            udp.Connect(endPoint);

            var request = new byte[48];
            request[0] = 0x1B;

            DateTime t0 = DateTime.UtcNow;
            WriteTimestamp(request, 40, t0);

            await udp.SendAsync(request, request.Length).ConfigureAwait(false);

            var receiveTask = udp.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(timeout)).ConfigureAwait(false)
                != receiveTask)
            {
                throw new TimeoutException("NTP request timed out.");
            }

            var result = receiveTask.Result;
            DateTime t3 = DateTime.UtcNow;
            var response = result.Buffer;

            if (response == null || response.Length < 48)
                throw new InvalidOperationException("Invalid NTP response.");

            DateTime originate = ReadTimestamp(response, 24);
            DateTime receive = ReadTimestamp(response, 32);
            DateTime transmit = ReadTimestamp(response, 40);

            double offsetMs = (
                (receive - t0).TotalMilliseconds + (transmit - t3).TotalMilliseconds
            ) / 2.0;

            DateTime corrected = t3.AddMilliseconds(offsetMs);
            return DateTime.SpecifyKind(corrected, DateTimeKind.Utc);
        }

        private static void WriteTimestamp(byte[] buffer, int offset, DateTime dt)
        {
            if (dt.Kind != DateTimeKind.Utc)
                dt = dt.ToUniversalTime();

            double totalSeconds = (dt - NtpEpoch).TotalSeconds;
            uint seconds = (uint)Math.Floor(totalSeconds);
            double fraction = totalSeconds - seconds;
            uint fractionPart = (uint)(fraction * 0x100000000L);

            buffer[offset + 0] = (byte)(seconds >> 24);
            buffer[offset + 1] = (byte)(seconds >> 16);
            buffer[offset + 2] = (byte)(seconds >> 8);
            buffer[offset + 3] = (byte)seconds;

            buffer[offset + 4] = (byte)(fractionPart >> 24);
            buffer[offset + 5] = (byte)(fractionPart >> 16);
            buffer[offset + 6] = (byte)(fractionPart >> 8);
            buffer[offset + 7] = (byte)fractionPart;
        }

        private static DateTime ReadTimestamp(byte[] buffer, int offset)
        {
            uint seconds = (uint)(
                (buffer[offset + 0] << 24)
                | (buffer[offset + 1] << 16)
                | (buffer[offset + 2] << 8)
                | buffer[offset + 3]
            );

            uint fraction = (uint)(
                (buffer[offset + 4] << 24)
                | (buffer[offset + 5] << 16)
                | (buffer[offset + 6] << 8)
                | buffer[offset + 7]
            );

            double fractionSeconds = fraction / (double)0x100000000L;
            double totalSeconds = seconds + fractionSeconds;
            var dt = NtpEpoch.AddSeconds(totalSeconds);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
    }
}

#endif
