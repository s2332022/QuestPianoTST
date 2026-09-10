using System.Diagnostics;
using System.Threading;

namespace QuestPianoMotion.Research
{
    /// <summary>Single monotonic clock shared by XR, MIDI, head pose and keyboard logs.</summary>
    public sealed class MonotonicSessionClock
    {
        static readonly double s_TickToSeconds = 1d / Stopwatch.Frequency;
        long m_SessionOriginTicks;
        double m_AndroidToStopwatchOffsetSeconds;

        public bool IsRunning { get; private set; }

        public double AbsoluteSeconds => Stopwatch.GetTimestamp() * s_TickToSeconds;

        public double SessionTimeSeconds => IsRunning
            ? (Stopwatch.GetTimestamp() - Interlocked.Read(ref m_SessionOriginTicks)) * s_TickToSeconds
            : 0d;

        public void StartSession()
        {
            Interlocked.Exchange(ref m_SessionOriginTicks, Stopwatch.GetTimestamp());
            IsRunning = true;
        }

        public void StopSession()
        {
            IsRunning = false;
        }

        /// <summary>Maps Android elapsedRealtimeNanos to the Stopwatch time domain.</summary>
        public void SynchronizeAndroidClock(long androidElapsedRealtimeNanos, double before, double after)
        {
            var midpoint = (before + after) * 0.5d;
            m_AndroidToStopwatchOffsetSeconds = midpoint - androidElapsedRealtimeNanos * 1e-9d;
        }

        public double AndroidNanosToAbsoluteSeconds(long androidElapsedRealtimeNanos)
        {
            return androidElapsedRealtimeNanos * 1e-9d + m_AndroidToStopwatchOffsetSeconds;
        }

        public double AbsoluteToSessionTime(double absoluteSeconds)
        {
            return IsRunning
                ? absoluteSeconds - Interlocked.Read(ref m_SessionOriginTicks) * s_TickToSeconds
                : 0d;
        }
    }
}
