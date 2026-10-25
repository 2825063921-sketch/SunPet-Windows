using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace MemePetsWindows
{
    public sealed class Clip
    {
        public int id { get; set; }
        public string label { get; set; }
        public string file { get; set; }
        public double duration { get; set; }
        public bool defaultOff { get; set; }
    }

    public sealed class VoicePlayer : IDisposable
    {
        readonly string directory;
        readonly PlaybackGate gate = new PlaybackGate();
        readonly DispatcherTimer deadline = new DispatcherTimer();
        MediaPlayer current;
        Clip clip;
        double volume = 0.65;
        public event Action<Clip> Started;
        public event Action<Clip, double> Opened;
        public event Action<Clip, string> Finished;
        public bool IsBusy { get { return gate.IsBusy; } }
        public double Volume
        {
            get { return volume; }
            set
            {
                if (Double.IsNaN(value) || Double.IsInfinity(value)) return;
                volume = Math.Max(0, Math.Min(1, value));
                if (current != null) current.Volume = volume;
            }
        }
        public VoicePlayer(string directory)
        {
            this.directory = directory;
            deadline.Tick += delegate { Finish(current, "error:播放超时，请重试"); };
        }
        public bool Play(Clip selected)
        {
            if (selected == null || !Program.SafeFileName(selected.file)) throw new ArgumentException("音频文件名无效");
            if (!gate.TryStart()) return false;
            var player = new MediaPlayer();
            current = player; clip = selected; player.Volume = volume;
            player.MediaOpened += delegate
            {
                if (current != player) return;
                if (!player.HasAudio || !player.NaturalDuration.HasTimeSpan)
                { Finish(player, "error:音频无法解码"); return; }
                double seconds = player.NaturalDuration.TimeSpan.TotalSeconds;
                if (seconds <= 0 || Double.IsInfinity(seconds) || seconds > 600)
                { Finish(player, "error:音频时长无效"); return; }
                deadline.Stop(); deadline.Interval = TimeSpan.FromSeconds(seconds + 5); deadline.Start();
                if (Opened != null) Opened(selected, seconds);
                if (current == player) player.Play();
            };
            player.MediaEnded += delegate { Finish(player, "ended"); };
            player.MediaFailed += delegate(object sender, ExceptionEventArgs error)
            { Finish(player, "error:" + error.ErrorException.Message); };
            try
            {
                string path = Path.Combine(directory, Path.ChangeExtension(selected.file, ".wav"));
                if (!File.Exists(path)) throw new FileNotFoundException("找不到原声音频", path);
                deadline.Interval = TimeSpan.FromSeconds(10); deadline.Start();
                if (Started != null) Started(selected);
                player.Open(new Uri(Path.GetFullPath(path), UriKind.Absolute));
                return true;
            }
            catch (Exception error) { Finish(player, "error:" + error.Message); return false; }
        }
        void Finish(MediaPlayer player, string reason)
        {
            if (player == null || current != player) return;
            Clip finished = clip; current = null; clip = null;
            deadline.Stop(); gate.Finish(); player.Close();
            if (Finished != null) Finished(finished, reason);
        }
        public void Stop() { Finish(current, "stopped"); }
        public void Dispose() { Stop(); deadline.Stop(); }
    }
}
