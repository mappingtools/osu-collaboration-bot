using System;
using System.Drawing;

namespace BeatmapHelper.BeatmapHelper {
    public class SpecialColour : ComboColour, IEquatable<SpecialColour>, ICloneable {
        public string Name {
            get;
            set;
        }

        public SpecialColour() {}

        public SpecialColour(Color color) : base(color) {}

        public SpecialColour(Color color, string name) : base(color) {
            Name = name;
        }

        public object Clone() {
            return new SpecialColour(Color, Name);
        }

        public bool Equals(SpecialColour other) {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Name == other.Name && Color == other.Color;
        }

        public override bool Equals(object obj) {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((SpecialColour) obj);
        }

        public override int GetHashCode() {
            return (Name != null ? Name.GetHashCode() : 0);
        }
    }
}