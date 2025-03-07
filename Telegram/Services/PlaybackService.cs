//
// Copyright Fela Ameghino 2015-2023
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using FFmpegInteropX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Streams;
using Telegram.Td.Api;
using Telegram.ViewModels;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Playback;

namespace Telegram.Services
{
    public interface IPlaybackService
    {
        IReadOnlyList<PlaybackItem> Items { get; }

        MessageWithOwner CurrentItem { get; }
        PlaybackItem CurrentPlayback { get; }

        double PlaybackSpeed { get; set; }

        double Volume { get; set; }

        void Pause();
        void Play();

        void MoveNext();
        void MovePrevious();

        void Seek(TimeSpan span);

        void Clear();

        void Play(MessageWithOwner message, long threadId = 0);

        TimeSpan Position { get; }
        TimeSpan Duration { get; }

        MediaPlaybackState PlaybackState { get; }



        bool? IsRepeatEnabled { get; set; }
        bool IsShuffleEnabled { get; set; }
        bool IsReversed { get; set; }



        event TypedEventHandler<IPlaybackService, MediaPlayerFailedEventArgs> MediaFailed;

        event TypedEventHandler<IPlaybackService, object> StateChanged;
        event TypedEventHandler<IPlaybackService, object> SourceChanged;
        event TypedEventHandler<IPlaybackService, object> PositionChanged;
        event TypedEventHandler<IPlaybackService, object> PlaylistChanged;
    }

    public class PlaybackService : IPlaybackService
    {
        private readonly ISettingsService _settingsService;

        private MediaPlayer _mediaPlayer;
        private double _lastTotalSeconds = double.MinValue;

        private readonly ConditionalWeakTable<IMediaPlaybackSource, PlaybackItem> _mapping;
        private CancellationTokenSource _nextToken;

        private long _threadId;

        private List<PlaybackItem> _items;

        public event TypedEventHandler<IPlaybackService, MediaPlayerFailedEventArgs> MediaFailed;
        public event TypedEventHandler<IPlaybackService, object> StateChanged;
        public event TypedEventHandler<IPlaybackService, object> SourceChanged;
        public event TypedEventHandler<IPlaybackService, object> PositionChanged;
        public event TypedEventHandler<IPlaybackService, object> PlaylistChanged;

        public PlaybackService(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            _isRepeatEnabled = _settingsService.Playback.RepeatMode == MediaPlaybackAutoRepeatMode.Track
                ? null
                : _settingsService.Playback.RepeatMode == MediaPlaybackAutoRepeatMode.List;
            _playbackSpeed = _settingsService.Playback.PlaybackRate;

            _mapping = new ConditionalWeakTable<IMediaPlaybackSource, PlaybackItem>();
        }

        #region SystemMediaTransportControls

        private void Transport_AutoRepeatModeChangeRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args)
        {
            IsRepeatEnabled = args.RequestedAutoRepeatMode == MediaPlaybackAutoRepeatMode.List
                ? true
                : args.RequestedAutoRepeatMode == MediaPlaybackAutoRepeatMode.Track
                ? null
                : false;
        }

        private void Transport_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Rewind:
                    Execute(player => player.StepBackwardOneFrame());
                    break;
                case SystemMediaTransportControlsButton.FastForward:
                    Execute(player => player.StepForwardOneFrame());
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    if (Position.TotalSeconds > 5)
                    {
                        Seek(TimeSpan.Zero);
                    }
                    else
                    {
                        MovePrevious();
                    }
                    break;
                case SystemMediaTransportControlsButton.Next:
                    MoveNext();
                    break;
            }
        }

        #endregion

        private void OnSourceChanged(MediaPlayer sender, object args)
        {
            if (sender.Source != null && _mapping.TryGetValue(sender.Source, out PlaybackItem item))
            {
                var message = item.Message;
                var webPage = message.Content is MessageText text ? text.WebPage : null;

                if ((message.Content is MessageVideoNote videoNote && !videoNote.IsViewed && !message.IsOutgoing) || (message.Content is MessageVoiceNote voiceNote && !voiceNote.IsListened && !message.IsOutgoing))
                {
                    message.ClientService.Send(new OpenMessageContent(message.ChatId, message.Id));
                }

                CurrentPlayback = item;
            }
        }

        private void OnMediaEnded(MediaPlayer sender, object args)
        {
            if (sender.Source != null && _mapping.TryGetValue(sender.Source, out PlaybackItem item))
            {
                if (item.Message.Content is MessageAudio && _isRepeatEnabled == null)
                {
                    Play();
                }
                else
                {
                    var index = _items.IndexOf(item);
                    if (index == -1 || index == (_isReversed ? 0 : _items.Count - 1))
                    {
                        if (item.Message.Content is MessageAudio && _isRepeatEnabled == true)
                        {
                            SetSource(_items[_isReversed ? _items.Count - 1 : 0], true);
                        }
                        else
                        {
                            Clear();
                        }
                    }
                    else
                    {
                        SetSource(_items[_isReversed ? index - 1 : index + 1], true);
                    }
                }
            }
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            Clear();
            MediaFailed?.Invoke(this, args);
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            if (sender.PlaybackState == MediaPlaybackState.Playing && sender.PlaybackRate != _playbackSpeed)
            {
                sender.PlaybackRate = _playbackSpeed;
            }

            switch (sender.PlaybackState)
            {
                case MediaPlaybackState.Playing:
                    sender.MediaPlayer.SystemMediaTransportControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;
                case MediaPlaybackState.Paused:
                    sender.MediaPlayer.SystemMediaTransportControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                case MediaPlaybackState.None:
                    sender.MediaPlayer.SystemMediaTransportControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    PlaybackState = MediaPlaybackState.None;
                    break;
            }
        }

        private void OnPositionChanged(MediaPlaybackSession sender, object args)
        {
            //var totalSeconds = Math.Truncate(sender.Position.TotalSeconds);
            //if (totalSeconds == _lastTotalSeconds)
            //{
            //    return;
            //}

            //_lastTotalSeconds = totalSeconds;
            PositionChanged?.Invoke(this, args);
        }

        private async void UpdateTransport()
        {
            var transport = _mediaPlayer?.SystemMediaTransportControls;
            if (transport == null)
            {
                return;
            }

            var items = _items;
            var item = CurrentPlayback;

            if (items == null || item?.Stream?.File == null)
            {
                transport.IsEnabled = false;
                transport.DisplayUpdater.ClearAll();
                return;
            }

            transport.IsEnabled = true;
            transport.IsPlayEnabled = true;
            transport.IsPauseEnabled = true;
            transport.IsPreviousEnabled = true;
            transport.IsNextEnabled = items.Count > 1;

            void SetProperties(string title, string artist)
            {
                transport.DisplayUpdater.ClearAll();
                transport.DisplayUpdater.Type = MediaPlaybackType.Music;

                try
                {
                    transport.DisplayUpdater.MusicProperties.Title = title ?? string.Empty;
                    transport.DisplayUpdater.MusicProperties.Artist = artist ?? string.Empty;
                }
                catch { }
            }

            if (item.Stream.File.Local.IsDownloadingCompleted)
            {
                try
                {
                    var cached = await item.Message.ClientService.GetFileAsync(item.Stream.File);
                    await transport.DisplayUpdater.CopyFromFileAsync(MediaPlaybackType.Music, cached);
                }
                catch
                {
                    SetProperties(item.Title, item.Performer);
                }
            }
            else
            {
                SetProperties(item.Title, item.Performer);
            }

            transport.DisplayUpdater.Update();
        }

        public IReadOnlyList<PlaybackItem> Items => _items ?? (IReadOnlyList<PlaybackItem>)Array.Empty<PlaybackItem>();

        private PlaybackItem _currentPlayback;
        public PlaybackItem CurrentPlayback
        {
            get => _currentPlayback;
            private set
            {
                _currentItem = value?.Message;
                _currentPlayback = value;
                SourceChanged?.Invoke(this, value);
                UpdateTransport();
            }
        }
        private MessageWithOwner _currentItem;
        public MessageWithOwner CurrentItem => _currentItem;

        public TimeSpan Position => Execute(player => player.PlaybackSession?.Position ?? TimeSpan.Zero, TimeSpan.Zero);

        public TimeSpan Duration => Execute(player => player.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero, TimeSpan.Zero);

        private MediaPlaybackState _playbackState;
        public MediaPlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                if (_playbackState != value)
                {
                    _playbackState = value;
                    StateChanged?.Invoke(this, null);
                }
            }
        }

        private bool? _isRepeatEnabled = false;
        public bool? IsRepeatEnabled
        {
            get => _isRepeatEnabled;
            set
            {
                _isRepeatEnabled = value;
                Execute(player => player.SystemMediaTransportControls.AutoRepeatMode = _settingsService.Playback.RepeatMode = value == true
                    ? MediaPlaybackAutoRepeatMode.List
                    : value == null
                    ? MediaPlaybackAutoRepeatMode.Track
                    : MediaPlaybackAutoRepeatMode.None);
            }
        }

        private bool _isReversed = false;
        public bool IsReversed
        {
            get => _isReversed;
            set => _isReversed = value;
        }

        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                _isShuffleEnabled = value;
                Execute(player => player.SystemMediaTransportControls.ShuffleEnabled = value);
            }
        }

        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = value;
                _settingsService.Playback.PlaybackRate = value;

                Execute(player =>
                {
                    player.PlaybackSession.PlaybackRate = value;
                    player.SystemMediaTransportControls.PlaybackRate = value;
                });
            }
        }

        public double Volume
        {
            get => _settingsService.VolumeLevel;
            set
            {
                _settingsService.VolumeLevel = value;
                Execute(player => player.Volume = value);
            }
        }

        public void Pause()
        {
            Execute(player =>
            {
                if (player.PlaybackSession.CanPause)
                {
                    player.Pause();
                    PlaybackState = MediaPlaybackState.Paused;
                }
            });
        }

        public void Play()
        {
            if (CurrentPlayback is PlaybackItem item)
            {
                PlaybackSpeed = item.CanChangePlaybackRate ? _settingsService.Playback.PlaybackRate : 1;
            }

            Execute(player =>
            {
                player.Play();
                PlaybackState = MediaPlaybackState.Playing;
            });
        }

        private void Execute(Action<MediaPlayer> action)
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    action(_mediaPlayer);
                }
                catch
                {
                    // All the remote procedure calls must be wrapped in a try-catch block
                }
            }
        }

        private T Execute<T>(Func<MediaPlayer, T> action, T defaultValue)
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    return action(_mediaPlayer);
                }
                catch
                {

                }
            }

            return defaultValue;
        }

        public void Seek(TimeSpan span)
        {
            Execute(player => player.PlaybackSession.Position = span);
        }

        public void MoveNext()
        {
            var items = _items;
            if (items == null)
            {
                return;
            }

            var index = items.IndexOf(CurrentPlayback);
            if (index == (_isReversed ? 0 : items.Count - 1))
            {
                SetSource(items, _isReversed ? items.Count - 1 : 0, true);
            }
            else
            {
                SetSource(items, _isReversed ? index - 1 : index + 1, true);
            }
        }

        public void MovePrevious()
        {
            var items = _items;
            if (items == null)
            {
                return;
            }

            var index = items.IndexOf(CurrentPlayback);
            if (index == (_isReversed ? items.Count - 1 : 0))
            {
                SetSource(items, _isReversed ? 0 : items.Count - 1, true);
            }
            else
            {
                SetSource(items, _isReversed ? index + 1 : index - 1, true);
            }
        }

        private void SetSource(List<PlaybackItem> items, int index, bool play = false)
        {
            if (index >= 0 && index <= items.Count - 1)
            {
                SetSource(items[index], play);
            }
        }

        private async void SetSource(PlaybackItem item, bool play = false)
        {
            try
            {
                Create();

                if (_mediaPlayer.Source != null && _mapping.TryGetValue(_mediaPlayer.Source, out PlaybackItem prevItem))
                {
                    _mapping.Remove(_mediaPlayer.Source);
                    prevItem.Dispose();
                }

                _nextToken?.Cancel();
                _mediaPlayer.Source = null;
                CurrentPlayback = item;

                var token = _nextToken = new CancellationTokenSource();

                var source = await item.GetSourceAsync();
                if (source != null && !token.IsCancellationRequested)
                {
                    if (_mediaPlayer != null)
                    {
                        _mapping.AddOrUpdate(source, item);
                        _mediaPlayer.Source = source;

                        if (play)
                        {
                            Play();
                        }

                        return;
                    }
                }

                item.Dispose();
            }
            catch
            {
                // All the remote procedure calls must be wrapped in a try-catch block
            }
        }

        public void Clear()
        {
            PlaybackState = MediaPlaybackState.None;

            //Execute.BeginOnUIThread(() => CurrentItem = null);
            CurrentPlayback = null;
            Dispose(true);
        }

        public async void Play(MessageWithOwner message, long threadId)
        {
            if (message == null)
            {
                return;
            }

            var previous = _items;
            if (previous != null && _threadId == threadId)
            {
                var already = previous.FirstOrDefault(x => x.Message.Id == message.Id && x.Message.ChatId == message.ChatId);
                if (already != null)
                {
                    SetSource(already, true);
                    return;
                }
            }

            Dispose(false);

            var item = GetPlaybackItem(message);
            var items = _items = new List<PlaybackItem>();

            _items.Add(item);
            _threadId = threadId;

            SetSource(item, true);

            if (message.Content is MessageText)
            {
                return;
            }

            var offset = -49;
            var filter = message.Content is MessageAudio ? new SearchMessagesFilterAudio() : (SearchMessagesFilter)new SearchMessagesFilterVoiceNote();

            var response = await message.ClientService.SendAsync(new SearchChatMessages(message.ChatId, string.Empty, null, message.Id, offset, 100, filter, _threadId));
            if (response is FoundChatMessages messages)
            {
                foreach (var add in message.Content is MessageAudio ? messages.Messages.OrderBy(x => x.Id) : messages.Messages.OrderByDescending(x => x.Id))
                {
                    if (add.Id > message.Id && add.Content is MessageAudio)
                    {
                        items.Insert(0, GetPlaybackItem(new MessageWithOwner(message.ClientService, add)));
                    }
                    else if (add.Id < message.Id && (add.Content is MessageVoiceNote || add.Content is MessageVideoNote))
                    {
                        items.Insert(0, GetPlaybackItem(new MessageWithOwner(message.ClientService, add)));
                    }
                }

                foreach (var add in message.Content is MessageAudio ? messages.Messages.OrderByDescending(x => x.Id) : messages.Messages.OrderBy(x => x.Id))
                {
                    if (add.Id < message.Id && add.Content is MessageAudio)
                    {
                        items.Add(GetPlaybackItem(new MessageWithOwner(message.ClientService, add)));
                    }
                    else if (add.Id > message.Id && (add.Content is MessageVoiceNote || add.Content is MessageVideoNote))
                    {
                        items.Add(GetPlaybackItem(new MessageWithOwner(message.ClientService, add)));
                    }
                }

                UpdateTransport();
                PlaylistChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private PlaybackItem GetPlaybackItem(MessageWithOwner message)
        {
            GetProperties(message, out File file, out int duration, out bool speed);

            var stream = new RemoteFileStream(message.ClientService, file, duration);
            var item = new PlaybackItem(stream)
            {
                Message = message,
                CanChangePlaybackRate = speed
            };

            if (message.Content is MessageAudio audio)
            {
                if (string.IsNullOrEmpty(audio.Audio.Performer) || string.IsNullOrEmpty(audio.Audio.Title))
                {
                    item.Title = audio.Audio.FileName;
                    item.Performer = string.Empty;
                }
                else
                {
                    item.Title = audio.Audio.Title;
                    item.Performer = audio.Audio.Performer;
                }
            }

            return item;
        }

        private void GetProperties(MessageWithOwner message, out File file, out int duration, out bool speed)
        {
            file = null;
            duration = 0;
            speed = false;

            if (message.Content is MessageAudio audio)
            {
                file = audio.Audio.AudioValue;
                duration = audio.Audio.Duration;
                speed = duration >= 10 * 60;
            }
            else if (message.Content is MessageVoiceNote voiceNote)
            {
                file = voiceNote.VoiceNote.Voice;
                duration = voiceNote.VoiceNote.Duration;
                speed = true;
            }
            else if (message.Content is MessageVideoNote videoNote)
            {
                file = videoNote.VideoNote.Video;
                duration = videoNote.VideoNote.Duration;
                speed = true;
            }
            else if (message.Content is MessageText text && text.WebPage != null)
            {
                if (text.WebPage.Audio != null)
                {
                    file = text.WebPage.Audio.AudioValue;
                    duration = text.WebPage.Audio.Duration;
                    speed = duration >= 10 * 60;
                }
                else if (text.WebPage.VoiceNote != null)
                {
                    file = text.WebPage.VoiceNote.Voice;
                    duration = text.WebPage.VoiceNote.Duration;
                    speed = true;
                }
                else if (text.WebPage.VideoNote != null)
                {
                    file = text.WebPage.VideoNote.Video;
                    duration = text.WebPage.VideoNote.Duration;
                    speed = true;
                }
            }
        }

        private void Dispose(bool full)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Source = null;
                _mediaPlayer.CommandManager.IsEnabled = false;

                if (full)
                {
                    _mediaPlayer.SystemMediaTransportControls.ButtonPressed -= Transport_ButtonPressed;
                    _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
                    _mediaPlayer.PlaybackSession.PositionChanged -= OnPositionChanged;
                    _mediaPlayer.MediaFailed -= OnMediaFailed;
                    _mediaPlayer.MediaEnded -= OnMediaEnded;
                    _mediaPlayer.SourceChanged -= OnSourceChanged;
                    _mediaPlayer.Dispose();

                    _mediaPlayer = null;
                }
            }

            if (_items != null)
            {
                foreach (var item in _items)
                {
                    item.Dispose();
                    item.Stream.Dispose();
                }

                _items = null;
            }

            _nextToken?.Cancel();
            _mapping?.Clear();
        }

        private void Create()
        {
            if (_mediaPlayer == null)
            {
                _mediaPlayer = new MediaPlayer();
                _mediaPlayer.SystemMediaTransportControls.AutoRepeatMode = _settingsService.Playback.RepeatMode;
                _mediaPlayer.SystemMediaTransportControls.ButtonPressed += Transport_ButtonPressed;
                _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
                _mediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;
                _mediaPlayer.MediaFailed += OnMediaFailed;
                _mediaPlayer.MediaEnded += OnMediaEnded;
                _mediaPlayer.SourceChanged += OnSourceChanged;
                _mediaPlayer.CommandManager.IsEnabled = false;
                _mediaPlayer.Volume = _settingsService.VolumeLevel;
            }
        }
    }

    public class PlaybackItem
    {
        private FFmpegMediaSource _ffmpeg;
        private IMediaPlaybackSource _source;

        public MessageWithOwner Message { get; set; }

        public RemoteFileStream Stream { get; set; }

        public string Title { get; set; }
        public string Performer { get; set; }

        public bool CanChangePlaybackRate { get; set; }

        public PlaybackItem(RemoteFileStream stream)
        {
            Stream = stream;
        }

        public async Task<IMediaPlaybackSource> GetSourceAsync()
        {
            try
            {
                if (_source == null)
                {
                    _ffmpeg = await FFmpegMediaSource.CreateFromStreamAsync(Stream);
                    _source = _ffmpeg.CreateMediaPlaybackItem();
                }

                return _source;
            }
            catch
            {
                Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                _ffmpeg?.Dispose();
            }
            catch
            {
                Logger.Error();
            }

            _ffmpeg = null;
            _source = null;
        }
    }
}
