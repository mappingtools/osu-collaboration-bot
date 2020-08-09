namespace BeatmapHelper.BeatmapHelper.Events {
    /// <summary>
    /// Indicates that a type has an end time. Used by Property Transformer on Events
    /// </summary>
    public interface IHasEndTime {
        int EndTime { get; set; }
    }
}