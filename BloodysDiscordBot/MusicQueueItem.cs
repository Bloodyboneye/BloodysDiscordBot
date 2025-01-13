namespace BloodysDiscordBot
{
    public class MusicQueueItem(string fileOrURL, string songName, bool isFile, string author, uint duration)
    {
        public readonly string fileOrURL = fileOrURL;

        public readonly string songName = songName;

        public readonly bool isFile = isFile;

        public readonly string author = author;

        public readonly uint duration = duration;
    }
}
