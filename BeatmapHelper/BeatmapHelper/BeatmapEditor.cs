using System.Collections.Generic;
using System.IO;

namespace BeatmapHelper.BeatmapHelper {
    public class BeatmapEditor : Editor
    {
        public Beatmap Beatmap => (Beatmap)TextFile;

        public BeatmapEditor(List<string> lines)
        {
            TextFile = new Beatmap(lines);
        }

        public BeatmapEditor(string path)
        {
            Path = path;
            TextFile = new Beatmap(ReadFile(Path));
        }

        /// <summary>
        /// Saves the beatmap just like <see cref="SaveFile()"/> but also updates the filename according to the metadata of the <see cref="Beatmap"/>
        /// </summary>
        /// <remarks>This method also updates the Path property</remarks>
        public void SaveFileWithNameUpdate() {
            // Remove the beatmap with the old filename
            File.Delete(Path);

            // Save beatmap with the new filename
            Path = System.IO.Path.Combine(GetParentFolder(), Beatmap.GetFileName());
            SaveFile();
        }
    }
}
