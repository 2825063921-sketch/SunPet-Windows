using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace MemePetsWindows
{
    public sealed class Preferences
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Volume { get; set; }
        public int FixedId { get; set; }
        public int Size { get; set; }
        public int[] EnabledIds { get; set; }
        public bool Linked { get; set; }
        public Preferences() { Volume = 0.65; Size = 1; Linked = true; }
    }

    public sealed class PetWindow : Window
    {
        readonly PetConfig config;
        readonly Clip[] clips;
        readonly VoicePlayer voice;
        readonly Diagnostics diagnostics;
        readonly Bridge bridge;
        readonly LinkReceiver receiver;
        readonly Preferences preferences;
        readonly string preferencesPath;
        readonly Random random = new Random();
        readonly Image picture = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.95) };
        readonly RotateTransform sway = new RotateTransform();
        readonly TextBlock caption = new TextBlock { FontSize = 14, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        readonly Border bubble;
        readonly Button faceButton;
        readonly RowDefinition faceRow;
        readonly DispatcherTimer animation = new DispatcherTimer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MenuItem fixedMode = new MenuItem { Header = "固定播放当前这一句", IsCheckable = true };
        readonly MenuItem randomMode = new MenuItem { Header = "随机播放（每轮不重复）", IsCheckable = true };
        readonly MenuItem linkMode = new MenuItem { Header = "与其他桌宠接话", IsCheckable = true };
        readonly MenuItem volumeMenu = new MenuItem { Header = "音量" };
        readonly MenuItem sizeMenu = new MenuItem { Header = "尺寸" };
        readonly MenuItem[] phraseItems;
        readonly MenuItem[] filterItems;
        PhrasePool manualPool, responsePool;
        LinkMessage context;
        int lastId;
        bool closing, loaded, warnedSave, responsePending;
        string settingsWarning;
        long hideCaption;
        Point pressPoint, pressWindowOrigin;
        bool pressing, dragged;

        public PetWindow(string root, PetConfig config, Clip[] clips, bool diagnosticMode)
        {
            this.config = config; this.clips = clips;
            diagnostics = new Diagnostics(config.role, diagnosticMode);
            preferencesPath = Path.Combine(diagnostics.DirectoryPath, "settings.json");
            preferences = LoadPreferences();
            receiver = new LinkReceiver(config.role);
            voice = new VoicePlayer(Path.Combine(root, "Assets", "Playback")); voice.Volume = preferences.Volume;
            ResetPools(); lastId = preferences.FixedId;
            Title = config.title;
            AllowsTransparency = true; WindowStyle = WindowStyle.None; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = diagnosticMode;
            ShowActivated = false; FontFamily = new FontFamily("Microsoft YaHei UI");
            picture.Source = NativeTests.LoadFace(root); picture.RenderTransform = sway;
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
            faceRow = new RowDefinition(); grid.RowDefinitions.Add(faceRow);
            bubble = new Border { Child = caption, Background = new SolidColorBrush(Color.FromRgb(250, 251, 253)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 222, 230)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13), Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 4, 4, 7), Visibility = Visibility.Hidden, IsHitTestVisible = false };
            grid.Children.Add(bubble);
            faceButton = new Button { Content = picture, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                ToolTip = "左键听一句 · 拖动换位置 · 右键选句、筛选和设置" };
            faceButton.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            AutomationProperties.SetName(faceButton, config.title + "，点击播放一句原声");
            Grid.SetRow(faceButton, 1); grid.Children.Add(faceButton); Content = grid;
            ApplySize();
            // Keyboard/accessibility activation still uses Button.Click. Pointer gestures
            // are handled below so a completed drag cannot also raise a playback click.
            faceButton.Click += delegate { PlayManual(preferences.FixedId > 0 ? preferences.FixedId : 0, false); };
            faceButton.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                pressPoint = PointToScreen(e.GetPosition(this));
                pressWindowOrigin = new Point(Left, Top); dragged = false;
                pressing = faceButton.CaptureMouse(); e.Handled = pressing;
            };
            faceButton.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!pressing) return;
                if (e.LeftButton != MouseButtonState.Pressed) { FinishPointerGesture(); return; }
                UpdatePointerGesture(PointToScreen(e.GetPosition(this)));
                e.Handled = dragged;
            };
            faceButton.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!pressing) return;
                // Include the final pointer position even for a fast, single-move drag.
                UpdatePointerGesture(PointToScreen(e.GetPosition(this)));
                bool click = !dragged;
                FinishPointerGesture(); e.Handled = true;
                if (click) PlayManual(preferences.FixedId > 0 ? preferences.FixedId : 0, false);
            };
            faceButton.LostMouseCapture += delegate { if (pressing) FinishPointerGesture(); };
            var menu = new ContextMenu();
            var phrases = new MenuItem { Header = "挑一句（" + clips.Length + " 条原声）" };
            var filters = new MenuItem { Header = "启用的音频（至少保留一条）" };
            phraseItems = new MenuItem[clips.Length]; filterItems = new MenuItem[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                int index = i;
                string label = String.Format("{0:00}  {1}  · {2:0.00} 秒", clips[i].id, clips[i].label, clips[i].duration);
                var phrase = new MenuItem { Header = label, IsCheckable = true };
                phrase.Click += delegate { PlayManual(clips[index].id, true); };
                phrases.Items.Add(phrase); phraseItems[i] = phrase;
                var filter = new MenuItem { Header = label, IsCheckable = true, StaysOpenOnClick = true };
                filter.Click += delegate
                {
                    var enabled = new HashSet<int>(preferences.EnabledIds);
                    if (filter.IsChecked) enabled.Add(clips[index].id);
                    else if (enabled.Count == 1 && enabled.Contains(clips[index].id))
                    { filter.IsChecked = true; ShowCaption("至少保留一条启用的音频。", 4000); return; }
                    else enabled.Remove(clips[index].id);
                    preferences.EnabledIds = enabled.OrderBy(id => id).ToArray();
                    if (!enabled.Contains(preferences.FixedId) && preferences.FixedId > 0)
                    { preferences.FixedId = 0; ShowCaption("固定句已停用，已切换为随机播放。", 5000); }
                    ResetPools(); SavePreferences(); UpdateMenu();
                };
                filters.Items.Add(filter); filterItems[i] = filter;
            }
            menu.Items.Add(phrases); menu.Items.Add(filters); menu.Items.Add(new Separator());
            randomMode.Click += delegate { preferences.FixedId = 0; SavePreferences(); };
            fixedMode.Click += delegate
            {
                preferences.FixedId = fixedMode.IsChecked && preferences.EnabledIds.Contains(lastId) ? lastId : 0;
                SavePreferences();
            };
            menu.Items.Add(randomMode); menu.Items.Add(fixedMode);
            foreach (double level in new[] { 0.0, 0.25, 0.50, 0.65, 0.75, 1.0 })
            {
                double selected = level;
                var item = new MenuItem { Header = level == 0 ? "静音（0%）" : (level * 100).ToString("0") + "%", Tag = level, IsCheckable = true };
                item.Click += delegate { voice.Volume = selected; SavePreferences(); };
                volumeMenu.Items.Add(item);
            }
            menu.Items.Add(volumeMenu);
            string[] sizes = { "小", "中", "大" };
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = i; var item = new MenuItem { Header = sizes[i], Tag = i, IsCheckable = true };
                item.Click += delegate { preferences.Size = size; ApplySize(); ClampPosition(); SavePreferences(); };
                sizeMenu.Items.Add(item);
            }
            menu.Items.Add(sizeMenu);
            linkMode.Click += delegate
            {
                preferences.Linked = linkMode.IsChecked;
                if (!preferences.Linked) { context = null; responsePending = false; }
                diagnostics.Write("drop", null, null, preferences.Linked ? "link-enabled" : "link-disabled-context-cleared");
                SavePreferences();
            };
            menu.Items.Add(linkMode); menu.Items.Add(new Separator());
            AddMenu(menu, "停止播放", delegate { CancelPlayback(); });
            AddMenu(menu, "移回默认位置", delegate { PlaceDefault(); SavePreferences(); });
            AddMenu(menu, "退出 " + config.title, delegate { Close(); });
            menu.Opened += delegate { UpdateMenu(); };
            faceButton.ContextMenu = menu;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) CancelPlayback(); };
            voice.Started += delegate(Clip clip)
            { diagnostics.Write("start", context, clip.id, null); ShowCaption(clip.label, Int32.MaxValue); };
            voice.Opened += delegate(Clip clip, double seconds) { diagnostics.Write("open", context, clip.id, seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)); };
            voice.Finished += delegate(Clip clip, string reason)
            {
                LinkMessage finishedContext = context; context = null;
                diagnostics.Write("finish", finishedContext, clip.id, reason); sway.Angle = 0;
                if (reason.StartsWith("error:")) ShowCaption(reason.Substring(6), 6000);
                else hideCaption = clock.ElapsedMilliseconds + 1400;
                if (reason == "ended" && preferences.Linked && !closing && finishedContext != null) bridge.Send(finishedContext, clip.id);
            };
            bridge = new Bridge(this, diagnostics);
            bridge.Received += delegate(LinkMessage message)
            {
                string reason;
                if (!receiver.TryAccept(message, DateTime.UtcNow, voice.IsBusy || responsePending || closing, preferences.Linked, out reason))
                { diagnostics.Write("drop", message, null, reason); return; }
                // Reserve this response now, then return from WM_COPYDATA before opening media.
                // A pending response counts as busy; stop/disable/exit invalidate the reservation.
                LinkMessage next = LinkRules.Advance(config.role, message);
                context = next; responsePending = true;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!responsePending || !Object.ReferenceEquals(context, next)) return;
                    responsePending = false;
                    string expiredReason;
                    if (closing || !preferences.Linked || voice.IsBusy || !LinkRules.Validate(next, DateTime.UtcNow, out expiredReason))
                    { context = null; diagnostics.Write("drop", next, null, "response-cancelled-or-expired"); return; }
                    diagnostics.Write("receive", message, null, null);
                    int id = config.role == "sun" ? responsePool.Next() : manualPool.Next();
                    PlayId(id);
                }));
            };
            animation.Interval = TimeSpan.FromMilliseconds(35);
            animation.Tick += delegate
            {
                sway.Angle = voice.IsBusy && SystemParameters.ClientAreaAnimation ? Math.Sin(clock.Elapsed.TotalSeconds * 9) * 3 : 0;
                if (!voice.IsBusy && clock.ElapsedMilliseconds >= hideCaption) bubble.Visibility = Visibility.Hidden;
            };
            Loaded += delegate
            {
                PlaceDefault();
                if (preferences.Left.HasValue && preferences.Top.HasValue) { Left = preferences.Left.Value; Top = preferences.Top.Value; ClampPosition(); }
                loaded = true;
                ShowCaption(settingsWarning ?? "点一下听原声，右键打开菜单。", settingsWarning == null ? 5000 : 10000);
                animation.Start();
            };
            Closing += delegate { closing = true; CancelPlayback(); };
            Closed += delegate { SavePreferences(); animation.Stop(); bridge.Dispose(); voice.Dispose(); };
        }

        static void AddMenu(ContextMenu menu, string label, Action action)
        { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        void UpdatePointerGesture(Point screenPoint)
        {
            Vector offset = screenPoint - pressPoint;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                offset = source.CompositionTarget.TransformFromDevice.Transform(offset);
            if (!dragged && offset.Length < 6) return;
            dragged = true;
            Left = pressWindowOrigin.X + offset.X;
            Top = pressWindowOrigin.Y + offset.Y;
        }
        void FinishPointerGesture()
        {
            pressing = false;
            if (faceButton.IsMouseCaptured) faceButton.ReleaseMouseCapture();
            if (dragged) { ClampPosition(); SavePreferences(); }
        }
        void ResetPools()
        {
            manualPool = new PhrasePool(preferences.EnabledIds, false, random);
            responsePool = new PhrasePool(preferences.EnabledIds, true, random);
        }
        void PlayManual(int id, bool replace)
        {
            if (!replace && (voice.IsBusy || responsePending)) { diagnostics.Write("drop", context, null, "busy-manual-click"); return; }
            if (replace) CancelPlayback();
            if (id == 0) id = manualPool.Next();
            if (!preferences.EnabledIds.Contains(id)) return;
            if (replace && preferences.FixedId > 0) { preferences.FixedId = id; SavePreferences(); }
            context = preferences.Linked ? LinkRules.Manual(config.role) : null;
            PlayId(id);
        }
        void PlayId(int id)
        {
            Clip clip = clips.First(c => c.id == id);
            if (voice.Play(clip)) lastId = id;
            else context = null;
        }
        void CancelPlayback() { context = null; responsePending = false; voice.Stop(); }
        void ShowCaption(string text, long milliseconds)
        { caption.Text = text; bubble.Visibility = Visibility.Visible; hideCaption = clock.ElapsedMilliseconds + milliseconds; }
        void UpdateMenu()
        {
            randomMode.IsChecked = preferences.FixedId == 0; fixedMode.IsChecked = preferences.FixedId > 0;
            fixedMode.IsEnabled = preferences.EnabledIds.Contains(lastId); linkMode.IsChecked = preferences.Linked;
            for (int i = 0; i < clips.Length; i++)
            {
                bool enabled = preferences.EnabledIds.Contains(clips[i].id);
                phraseItems[i].IsChecked = clips[i].id == lastId; phraseItems[i].IsEnabled = enabled; filterItems[i].IsChecked = enabled;
            }
            foreach (MenuItem item in volumeMenu.Items) item.IsChecked = Math.Abs((double)item.Tag - voice.Volume) < 0.001;
            foreach (MenuItem item in sizeMenu.Items) item.IsChecked = (int)item.Tag == preferences.Size;
        }
        void ApplySize()
        {
            double size = new[] { 140.0, 180.0, 230.0 }[preferences.Size];
            picture.Width = size; picture.Height = size; faceButton.Width = size; faceButton.Height = size;
            faceRow.Height = new GridLength(size + 8); Width = Math.Max(264, size + 44); Height = size + 96;
            bubble.MaxWidth = Width - 10;
        }
        Rect[] WorkAreas()
        {
            Matrix transform = Matrix.Identity;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null) transform = source.CompositionTarget.TransformFromDevice;
            return Forms.Screen.AllScreens.Select(screen => {
                System.Drawing.Rectangle area = screen.WorkingArea;
                Point start = transform.Transform(new Point(area.Left, area.Top));
                Point end = transform.Transform(new Point(area.Right, area.Bottom));
                return new Rect(start, end);
            }).ToArray();
        }
        void PlaceDefault()
        {
            Rect area = SystemParameters.WorkArea;
            int slot = config.role == "sun" ? 0 : config.role == "shaolan" ? 1 : 2;
            int columns = Math.Max(1, (int)((area.Width - 24) / (Width + 12)));
            Left = area.Right - Width - 16 - (slot % columns) * (Width + 12);
            Top = area.Bottom - Height - 12 - (slot / columns) * (Height + 8);
            ClampPosition();
        }
        void ClampPosition()
        {
            Rect[] areas = WorkAreas();
            if (areas.Length == 0) return;
            Rect current = new Rect(Left, Top, Width, Height);
            Rect best = areas.OrderByDescending(area => {
                Rect intersection = Rect.Intersect(area, current); return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            }).ThenBy(area => Math.Pow(area.X + area.Width / 2 - Left, 2) + Math.Pow(area.Y + area.Height / 2 - Top, 2)).First();
            Left = Math.Max(best.Left, Math.Min(Left, best.Right - Width));
            Top = Math.Max(best.Top, Math.Min(Top, best.Bottom - Height));
        }
        Preferences LoadPreferences()
        {
            var defaults = new Preferences { EnabledIds = config.defaultIds.ToArray() };
            if (!File.Exists(preferencesPath)) return defaults;
            try
            {
                string json = File.ReadAllText(preferencesPath, Encoding.UTF8);
                if (json.Length > 65536) throw new InvalidDataException("设置文件过大");
                var saved = new JavaScriptSerializer().Deserialize<Preferences>(json);
                if (saved == null || Double.IsNaN(saved.Volume) || Double.IsInfinity(saved.Volume) || saved.Volume < 0 || saved.Volume > 1 ||
                    saved.Size < 0 || saved.Size > 2 || saved.EnabledIds == null || saved.EnabledIds.Length == 0 ||
                    saved.EnabledIds.Any(id => !clips.Any(c => c.id == id)) ||
                    (saved.Left.HasValue && (Double.IsNaN(saved.Left.Value) || Double.IsInfinity(saved.Left.Value))) ||
                    (saved.Top.HasValue && (Double.IsNaN(saved.Top.Value) || Double.IsInfinity(saved.Top.Value))))
                    throw new InvalidDataException("设置内容无效");
                saved.EnabledIds = saved.EnabledIds.Distinct().OrderBy(id => id).ToArray();
                if (!saved.EnabledIds.Contains(saved.FixedId)) saved.FixedId = 0;
                return saved;
            }
            catch (Exception error)
            {
                settingsWarning = "设置读取失败，已恢复默认值：" + error.Message;
                diagnostics.Write("drop", null, null, "settings-read:" + error.Message); return defaults;
            }
        }
        void SavePreferences()
        {
            if (!loaded) return;
            try
            {
                preferences.Left = Left; preferences.Top = Top; preferences.Volume = voice.Volume;
                Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath));
                string temporary = preferencesPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(preferences), Encoding.UTF8);
                if (File.Exists(preferencesPath)) File.Replace(temporary, preferencesPath, null);
                else File.Move(temporary, preferencesPath);
                warnedSave = false;
            }
            catch (Exception error)
            {
                string message = "设置未能保存，下次启动可能恢复旧设置：" + error.Message;
                diagnostics.Write("drop", null, null, "settings-write:" + error.Message);
                if (!closing) ShowCaption(message, 12000);
                if (!warnedSave) { warnedSave = true; MessageBox.Show(message, config.title, MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }
    }
}
