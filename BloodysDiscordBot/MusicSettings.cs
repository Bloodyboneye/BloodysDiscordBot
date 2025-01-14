namespace BloodysDiscordBot
{
    public enum LoopType
    {
        None = 0,
        CurrentSong = 1,
        CurrentQueue = 2,
    }

    public enum AudioFilter
    {
        BassBost,
        Pitch,
        Tempo,
        Nightcore,
        Slowdown,
        Reverb,
        Chorus,
        Distortion,
        Flanger,
        Tremolo,
        Vibrato,
        Phaser,
    }

    public class MusicFilter(string filter, string filterName)
    {
        public string musicFilter = filter;

        public string filterName = filterName;

        public float? strength;

        public static MusicFilter BassFilter(float gain = 10, uint frequency = 100, float bandwidth = 0.3f) => new ($"bass=g={gain}:f={frequency}:w={bandwidth}", "Bass");

        public static MusicFilter PitchFilter(float pitch = 1) => new ($"rubberband=pitch={pitch}", "Pitch");

        public static MusicFilter TempoFilter(float tempo = 1) => new($"atempo={Math.Clamp(tempo, 0.5f, 2.0f)}", "Tempo");

        public static MusicFilter NightcoreFilter(float pitch = 1.15f, float tempo = 1.25f) => new($"asetrate={(uint)(48000f * pitch)}, atempo={Math.Clamp(tempo, 0.5f, 2.0f)}", "Nightcore");

        public static MusicFilter SlowdownFilter(float pitch = 0.83f, float tempo = 0.87f) => new($"asetrate={(uint)(48000f * pitch)}, atempo={Math.Clamp(tempo, 0.5f, 2.0f)}", "Slowdown");

        public static MusicFilter ReverbFilter(float inGain = 0.8f, float outGain = 0.9f, float delays = 1000, float decays = 0.3f) => new($"aecho={inGain}:{outGain}:{Math.Clamp(delays, 0f, 90000f)}:{Math.Clamp(decays, 0.0f, 1.0f)}", "Reverb");

        public static MusicFilter ChorusFilter(float inGain = 0.5f, float outGain = 0.9f, float delays = 60, float decays = 0.4f, float speeds = 0.25f, float depths = 2f) => new($"chorus={inGain}:{outGain}:{delays}:{decays}:{speeds}:{depths}", "Chorus");

        public static MusicFilter DistortionFilter() => new("acrusher=bits=4:mix=0.8,volume=3.0,firequalizer=gain='if(between(f,1000,2000),15,0)'", "Distortion");

        public static MusicFilter FlangerFilter() => new("flanger", "Flanger");

        public static MusicFilter TremoloFilter(float frequenzy = 5f, float depth = 0.8f) => new($"tremolo=f={Math.Clamp(frequenzy, 0.1f, 20000f)}:d={Math.Clamp(depth, 0.0f, 1.0f)}", "Tremolo");

        public static MusicFilter VibratoFilter(float frequenzy = 5f, float depth = 0.1f) => new($"vibrato=f={Math.Clamp(frequenzy, 0.1f, 20000f)}:d={Math.Clamp(depth, 0.0f, 1.0f)}", "Vibrato");

        public static MusicFilter PhaserFilter() => new($"aphaser=in_gain=0.9:out_gain=1.0:delay=4.5:decay=0.8:speed=1.5:type=t", "Phaser");
    }
}
