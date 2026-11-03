using System;
using System.Collections.Generic;

namespace TimeTheft
{
    // 이번 CloseAndShuffle의 출발 슬롯 ID만 보관한다. Face/RNG/AI 관찰은 포함하지 않는다.
    public sealed class ClockShuffle
    {
        public IReadOnlyList<int> DestinationToSource { get; }
        public IReadOnlyList<int> SourceToDestination { get; }
        internal ClockShuffle(int[] destinationToSource)
        {
            var sourceToDestination = new int[destinationToSource.Length];
            for (int destination = 0; destination < destinationToSource.Length; destination++)
                sourceToDestination[destinationToSource[destination]] = destination;
            DestinationToSource = Array.AsReadOnly((int[])destinationToSource.Clone());
            SourceToDestination = Array.AsReadOnly(sourceToDestination);
        }
    }
}
