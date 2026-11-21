using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CollaborationBot.Entities;
using Mapping_Tools.Core.BeatmapHelper;
using Mapping_Tools.Core.Tools.PatternGallery;
using Mapping_Tools.Core.Tools.PatternGallery.Models;

namespace CollaborationBot.Services {
    internal static class BeatmapSubmission {
        private const int PartMargin = 5;

        internal static async Task<int> SubmitPartAsync(string baseFilePath, string beatmapString,
            IReadOnlyList<Part> allowedParts, bool overwriteContinuously,
            Func<Beatmap, Beatmap, Task> reportWarnings = null) {
            var partBeatmap = BeatmapFile.Decode(beatmapString);
            var unlockedParts = allowedParts?.Where(p => p.Status != PartStatus.Locked).ToArray();

            if (unlockedParts != null) {
                partBeatmap.HitObjects = partBeatmap.HitObjects
                    .Where(obj => unlockedParts.Any(part => IsWithinPart(part, obj.Time, PartMargin) &&
                        IsWithinPart(part, obj.GetEndTime(false), PartMargin)))
                    .ToList();
            }

            var count = partBeatmap.HitObjects.Count;
            if (count == 0) {
                return 0;
            }

            var beatmap = BeatmapFile.Read(baseFilePath);
            if (reportWarnings != null) {
                await reportWarnings(partBeatmap, beatmap);
            }

            var placer = new PatternGalleryPlacer {
                PatternOverwriteMode = overwriteContinuously
                    ? PatternOverwriteMode.CompleteOverwrite
                    : PatternOverwriteMode.PartitionedOverwrite,
                TimingOverwriteMode = TimingOverwriteMode.PatternTimingOnly,
                Padding = 0,
                PartingDistance = 4,
                FixColourHax = true,
                FixBpmSv = false,
                FixStackLeniency = false,
                FixTickRate = false,
                FixGlobalSv = true,
                SnapToNewTiming = false,
                ScaleToNewTiming = false,
                IncludeHitsounds = true,
                IncludeKiai = true,
                ScaleToNewCircleSize = false,
            };

            if (!overwriteContinuously && unlockedParts != null) {
                foreach (var objects in GetAssignedPatterns(partBeatmap.HitObjects, unlockedParts)) {
                    partBeatmap.HitObjects = objects;
                    placer.PlaceOsuPattern(partBeatmap, beatmap);
                }
            } else if (overwriteContinuously && unlockedParts is { Length: > 0 }) {
                var (start, end) = GetNamedOverwriteBounds(unlockedParts[0], partBeatmap.HitObjects);
                placer.PlaceOsuPattern(partBeatmap, beatmap, overwriteStartTime: start, overwriteEndTime: end);
            } else {
                placer.PlaceOsuPattern(partBeatmap, beatmap);
            }

            beatmap.FixBreakPeriods();
            BeatmapFile.Write(baseFilePath, beatmap);
            return count;
        }

        private static bool IsWithinPart(Part part, double time, double margin) {
            return (!part.Start.HasValue || time >= part.Start.Value - margin) &&
                   (!part.End.HasValue || time <= part.End.Value + margin);
        }

        private static (double? Start, double? End) GetNamedOverwriteBounds(Part part, List<HitObject> submittedObjects) {
            double? start = part.Start is { } partStart
                ? Math.Min(partStart + (double)PartMargin, submittedObjects.Min(obj => obj.Time))
                : null;
            double? end = part.End is { } partEnd
                ? Math.Max(partEnd - (double)PartMargin, submittedObjects.Max(obj => obj.Time))
                : null;
            return (start, end);
        }

        private static IEnumerable<List<HitObject>> GetAssignedPatterns(List<HitObject> submittedObjects, Part[] parts) {
            var ranges = GetPlacementRanges(parts);
            var groups = submittedObjects.GroupBy(obj => {
                var end = obj.GetEndTime(false);
                var firstTime = Math.Min(obj.Time, end);
                var lastTime = Math.Max(obj.Time, end);
                return ranges.MinBy(range => Math.Max(range.Start - firstTime, lastTime - range.End));
            }).Select(group => (
                Start: group.Min(obj => obj.Time),
                End: group.Max(obj => Math.Max(obj.Time, obj.GetEndTime(false))),
                Objects: group.ToList()
            )).OrderBy(group => group.Start);

            var pending = new List<HitObject>();
            var pendingEnd = double.NegativeInfinity;
            foreach (var group in groups) {
                if (pending.Count > 0 && group.Start > pendingEnd) {
                    yield return pending.OrderBy(obj => obj).ToList();
                    pending = new List<HitObject>();
                }

                // The margin can let an object extend into another group's pattern.
                pending.AddRange(group.Objects);
                pendingEnd = Math.Max(pendingEnd, group.End);
            }

            if (pending.Count > 0) {
                yield return pending.OrderBy(obj => obj).ToList();
            }
        }

        private static List<(double Start, double End)> GetPlacementRanges(IEnumerable<Part> parts) {
            var ranges = new List<(double Start, double End)>();
            foreach (var part in parts.OrderBy(part => part.Start)) {
                var start = part.Start ?? double.NegativeInfinity;
                var end = part.End ?? double.PositiveInfinity;
                if (ranges.Count == 0 || start > ranges[^1].End) {
                    ranges.Add((start, end));
                } else {
                    // Merge intersecting ranges so a later placement cannot erase objects placed at a shared time.
                    ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
                }
            }
            return ranges;
        }
    }
}
