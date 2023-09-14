using System.Collections.Generic;

namespace Mission.TimeTable.Domain.Model.Comparer
{
    public class EveryXMinuteCompressionIntervalComparer : IEqualityComparer<CompressionInterval>
    {
        public bool Equals(CompressionInterval x, CompressionInterval y)
        {
            return x.Interval.Equals(y.Interval);
        }

        public int GetHashCode(CompressionInterval obj)
        {
            return obj.Interval.GetHashCode();
        }
    }
}
