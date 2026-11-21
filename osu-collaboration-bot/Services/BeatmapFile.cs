using System.IO;
using Mapping_Tools.Core.BeatmapHelper;
using Mapping_Tools.Core.BeatmapHelper.Serialization;

namespace CollaborationBot.Services {
    internal static class BeatmapFile {
        internal static Beatmap Decode(string text) => new BeatmapDecoder().Decode(text);

        internal static Beatmap Read(string path) => Decode(File.ReadAllText(path));

        internal static void Write(string path, Beatmap beatmap) => File.WriteAllText(path, new BeatmapEncoder().Encode(beatmap));
    }
}
