namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    using System;
    using System.Net;
    using System.Net.Sockets;

    public static class NtpClient
    {
        private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime GetNetworkTime(string host, int timeout = 3000)
        {
            const int NtpPacketSize = 48;
            const int TransmitTimestampOffset = 40;
            const int ReceiveTimestampOffset = 32;
            const int OriginateTimestampOffset = 24;

            var ipAddresses = Dns.GetHostAddresses(host);
            if (ipAddresses == null || ipAddresses.Length == 0)
                throw new ArgumentException("Could not resolve host.", nameof(host));

            var endPoint = new IPEndPoint(ipAddresses[0], 123);
            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = timeout;

                var request = new byte[NtpPacketSize];
                request[0] = 0x1B;

                DateTime t0 = DateTime.UtcNow;
                WriteTimestamp(request, TransmitTimestampOffset, t0);

                udp.Connect(endPoint);
                udp.Send(request, request.Length);

                var responseEP = new IPEndPoint(IPAddress.Any, 0);
                var response = udp.Receive(ref responseEP);
                DateTime t3 = DateTime.UtcNow;

                if (response == null || response.Length < NtpPacketSize)
                    throw new InvalidOperationException("Invalid NTP response.");

                DateTime originate = ReadTimestamp(response, OriginateTimestampOffset);
                DateTime receive = ReadTimestamp(response, ReceiveTimestampOffset);
                DateTime transmit = ReadTimestamp(response, TransmitTimestampOffset);
                double offsetMs = ((receive - t0).TotalMilliseconds + (transmit - t3).TotalMilliseconds) / 2.0;
                DateTime corrected = t3.AddMilliseconds(offsetMs);
                return DateTime.SpecifyKind(corrected, DateTimeKind.Utc);
            }
        }

        public static async System.Threading.Tasks.Task<DateTime> GetNetworkTimeAsync(string host, int timeout = 3000)
        {
            const int NtpPacketSize = 48;
            const int TransmitTimestampOffset = 40;
            const int ReceiveTimestampOffset = 32;
            const int OriginateTimestampOffset = 24;

            var ipAddresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (ipAddresses == null || ipAddresses.Length == 0)
                throw new ArgumentException("Could not resolve host.", nameof(host));

            var endPoint = new IPEndPoint(ipAddresses[0], 123);
            using (var udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = timeout;
                udp.Connect(endPoint);

                var request = new byte[NtpPacketSize];
                request[0] = 0x1B;
                DateTime t0 = DateTime.UtcNow;
                WriteTimestamp(request, TransmitTimestampOffset, t0);

                await udp.SendAsync(request, request.Length).ConfigureAwait(false);

                var receiveTask = udp.ReceiveAsync();
                if (await System.Threading.Tasks.Task.WhenAny(receiveTask, System.Threading.Tasks.Task.Delay(timeout)).ConfigureAwait(false) != receiveTask)
                    throw new TimeoutException("NTP request timed out.");

                var result = receiveTask.Result;
                DateTime t3 = DateTime.UtcNow;
                var response = result.Buffer;

                if (response == null || response.Length < NtpPacketSize)
                    throw new InvalidOperationException("Invalid NTP response.");

                DateTime originate = ReadTimestamp(response, OriginateTimestampOffset);
                DateTime receive = ReadTimestamp(response, ReceiveTimestampOffset);
                DateTime transmit = ReadTimestamp(response, TransmitTimestampOffset);

                double offsetMs = ((receive - t0).TotalMilliseconds + (transmit - t3).TotalMilliseconds) / 2.0;
                DateTime corrected = t3.AddMilliseconds(offsetMs);
                return DateTime.SpecifyKind(corrected, DateTimeKind.Utc);
            }
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
            buffer[offset + 3] = (byte)(seconds);

            buffer[offset + 4] = (byte)(fractionPart >> 24);
            buffer[offset + 5] = (byte)(fractionPart >> 16);
            buffer[offset + 6] = (byte)(fractionPart >> 8);
            buffer[offset + 7] = (byte)(fractionPart);
        }

        private static DateTime ReadTimestamp(byte[] buffer, int offset)
        {
            uint seconds = (uint)(
                (buffer[offset + 0] << 24) |
                (buffer[offset + 1] << 16) |
                (buffer[offset + 2] << 8) |
                (buffer[offset + 3])
            );

            uint fraction = (uint)(
                (buffer[offset + 4] << 24) |
                (buffer[offset + 5] << 16) |
                (buffer[offset + 6] << 8) |
                (buffer[offset + 7])
            );

            double fractionSeconds = fraction / (double)0x100000000L;
            double totalSeconds = seconds + fractionSeconds;
            var dt = NtpEpoch.AddSeconds(totalSeconds);
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
    }
}