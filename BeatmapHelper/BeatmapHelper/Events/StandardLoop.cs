﻿using System.Text;
using static BeatmapHelper.BeatmapHelper.FileFormatHelper;

namespace BeatmapHelper.BeatmapHelper.Events {
    /// <summary>
    /// Represents the standard loop event. This event has a different syntax so it can't be a <see cref="OtherCommand"/>.
    /// </summary>
    public class StandardLoop : Command {
        public override EventType EventType => EventType.L;

        public int LoopCount { get; set; }

        public override string GetLine() {
            return $"{EventType},{StartTime.ToInvariant()},{LoopCount.ToInvariant()}";
        }

        public override void SetLine(string line) {
            var subLine = RemoveIndents(line);
            var values = subLine.Split(',');

            if (TryParseInt(values[1], out int startTime))
                StartTime = startTime;
            else throw new BeatmapParsingException("Failed to parse start time of event param.", line);

            if (TryParseInt(values[2], out int loopCount))
                LoopCount = loopCount;
            else throw new BeatmapParsingException("Failed to parse loop count of event param.", line);
        }
    }
}