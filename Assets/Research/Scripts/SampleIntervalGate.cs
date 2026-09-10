using System;

namespace QuestPianoMotion.Research
{
    /// <summary>Throttles work using caller-supplied monotonic time.</summary>
    public sealed class SampleIntervalGate
    {
        double m_IntervalSeconds;
        double m_NextSampleTime;
        bool m_HasSampled;

        /// <summary>Creates a gate with the specified interval in seconds.</summary>
        public SampleIntervalGate(double intervalSeconds)
        {
            IntervalSeconds = intervalSeconds;
        }

        /// <summary>Gets or sets the minimum interval in seconds.</summary>
        public double IntervalSeconds
        {
            get => m_IntervalSeconds;
            set => m_IntervalSeconds = Math.Max(0.01d, value);
        }

        /// <summary>Returns true when work is due at the supplied monotonic time.</summary>
        public bool ShouldSample(double monotonicTimeSeconds)
        {
            if (!m_HasSampled || monotonicTimeSeconds >= m_NextSampleTime)
            {
                m_HasSampled = true;
                m_NextSampleTime = monotonicTimeSeconds + m_IntervalSeconds;
                return true;
            }

            return false;
        }

        /// <summary>Makes the next call to <see cref="ShouldSample"/> return true.</summary>
        public void Reset()
        {
            m_HasSampled = false;
            m_NextSampleTime = 0d;
        }
    }
}
