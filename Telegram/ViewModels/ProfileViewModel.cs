//
// Copyright Fela Ameghino 2015-2023
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Collections;
using Telegram.Common;
using Telegram.Converters;
using Telegram.Navigation.Services;
using Telegram.Services;
using Telegram.Td.Api;
using Telegram.ViewModels.Chats;
using Telegram.ViewModels.Delegates;
using Telegram.ViewModels.Supergroups;
using Telegram.ViewModels.Users;
using Telegram.Views;
using Telegram.Views.Chats;
using Telegram.Views.Popups;
using Telegram.Views.Premium.Popups;
using Telegram.Views.Supergroups;
using Telegram.Views.Supergroups.Popup;
using Telegram.Views.Users;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Telegram.ViewModels
{
    public class ProfileViewModel : ChatSharedMediaViewModel, IDelegable<IProfileDelegate>, IHandle
    {
        public string LastSeen { get; internal set; }

        public IProfileDelegate Delegate { get; set; }

        private readonly IVoipService _voipService;
        private readonly IVoipGroupService _voipGroupService;
        private readonly INotificationsService _notificationsService;
        private readonly ITranslateService _translateService;

        private readonly UserCommonChatsViewModel _userCommonChatsViewModel;
        private readonly SupergroupMembersViewModel _supergroupMembersVieModel;

        public ProfileViewModel(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator, IPlaybackService playbackService, IVoipService voipService, IVoipGroupService voipGroupService, INotificationsService notificationsService, IStorageService storageService, ITranslateService translateService, ChatSharedMediaViewModel chatSharedMediaViewModel, UserCommonChatsViewModel userCommonChatsViewModel, SupergroupMembersViewModel supergroupMembersViewModel)
            : base(clientService, settingsService, storageService, aggregator, playbackService)
        {
            _voipService = voipService;
            _voipGroupService = voipGroupService;
            _notificationsService = notificationsService;
            _translateService = translateService;

            _userCommonChatsViewModel = userCommonChatsViewModel;
            _supergroupMembersVieModel = supergroupMembersViewModel;
            _supergroupMembersVieModel.IsEmbedded = true;

            SetTimerCommand = new RelayCommand<int?>(SetTimer);

            Children.Add(userCommonChatsViewModel);
            Children.Add(supergroupMembersViewModel);
        }

        public ITranslateService TranslateService => _translateService;

        public UserCommonChatsViewModel UserCommonChats => _userCommonChatsViewModel;
        public SupergroupMembersViewModel SupergroupMembers => _supergroupMembersVieModel;

        protected ObservableCollection<ChatMember> _members;
        public ObservableCollection<ChatMember> Members
        {
            get => _members;
            set => Set(ref _members, value);
        }

        protected override Task OnNavigatedToAsync(object parameter, NavigationMode mode, NavigationState state)
        {
            if (parameter is string pair)
            {
                var split = pair.Split(';');
                if (split.Length != 2)
                {
                    return Task.CompletedTask;
                }

                var failed1 = !long.TryParse(split[0], out long result1);
                var failed2 = !long.TryParse(split[1], out long result2);

                if (failed1 || failed2)
                {
                    return Task.CompletedTask;
                }

                parameter = result1;

                if (ClientService.TryGetTopicInfo(result1, result2, out ForumTopicInfo info))
                {
                    Topic = info;
                }
            }


            var chatId = (long)parameter;

            Chat = ClientService.GetChat(chatId);

            var chat = _chat;
            if (chat == null)
            {
                return Task.CompletedTask;
            }

            //Subscribe();
            Delegate?.UpdateChat(chat);

            if (chat.Type is ChatTypePrivate privata)
            {
                var item = ClientService.GetUser(privata.UserId);
                var cache = ClientService.GetUserFull(privata.UserId);

                Delegate?.UpdateUser(chat, item, false);

                if (cache == null)
                {
                    ClientService.Send(new GetUserFullInfo(privata.UserId));
                }
                else
                {
                    Delegate?.UpdateUserFullInfo(chat, item, cache, false, false);
                }
            }
            else if (chat.Type is ChatTypeSecret secretType)
            {
                var secret = ClientService.GetSecretChat(secretType.SecretChatId);
                var item = ClientService.GetUser(secretType.UserId);
                var cache = ClientService.GetUserFull(secretType.UserId);

                Delegate?.UpdateSecretChat(chat, secret);
                Delegate?.UpdateUser(chat, item, true);

                if (cache == null)
                {
                    ClientService.Send(new GetUserFullInfo(secret.UserId));
                }
                else
                {
                    Delegate?.UpdateUserFullInfo(chat, item, cache, true, false);
                }
            }
            else if (chat.Type is ChatTypeBasicGroup basic)
            {
                var item = ClientService.GetBasicGroup(basic.BasicGroupId);
                var cache = ClientService.GetBasicGroupFull(basic.BasicGroupId);

                Delegate?.UpdateBasicGroup(chat, item);

                if (cache == null)
                {
                    ClientService.Send(new GetBasicGroupFullInfo(basic.BasicGroupId));
                }
                else
                {
                    Delegate?.UpdateBasicGroupFullInfo(chat, item, cache);
                }
            }
            else if (chat.Type is ChatTypeSupergroup super)
            {
                var item = ClientService.GetSupergroup(super.SupergroupId);
                var cache = ClientService.GetSupergroupFull(super.SupergroupId);

                Delegate?.UpdateSupergroup(chat, item);

                if (cache == null)
                {
                    ClientService.Send(new GetSupergroupFullInfo(super.SupergroupId));
                }
                else
                {
                    Delegate?.UpdateSupergroupFullInfo(chat, item, cache);
                }
            }

            return base.OnNavigatedToAsync(parameter, mode, state);
        }

        public override void Subscribe()
        {
            Aggregator.Subscribe<UpdateUser>(this, Handle)
                .Subscribe<UpdateUserFullInfo>(Handle)
                .Subscribe<UpdateBasicGroup>(Handle)
                .Subscribe<UpdateBasicGroupFullInfo>(Handle)
                .Subscribe<UpdateSupergroup>(Handle)
                .Subscribe<UpdateSupergroupFullInfo>(Handle)
                .Subscribe<UpdateUserStatus>(Handle)
                .Subscribe<UpdateChatTitle>(Handle)
                .Subscribe<UpdateChatPhoto>(Handle)
                .Subscribe<UpdateChatNotificationSettings>(Handle);
        }



        public void Handle(UpdateUser update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata && privata.UserId == update.User.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateUser(chat, update.User, false));
            }
            else if (chat.Type is ChatTypeSecret secret && secret.UserId == update.User.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateUser(chat, update.User, true));
            }
        }

        public void Handle(UpdateUserFullInfo update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata && privata.UserId == update.UserId)
            {
                BeginOnUIThread(() => Delegate?.UpdateUserFullInfo(chat, ClientService.GetUser(update.UserId), update.UserFullInfo, false, false));
            }
            else if (chat.Type is ChatTypeSecret secret && secret.UserId == update.UserId)
            {
                BeginOnUIThread(() => Delegate?.UpdateUserFullInfo(chat, ClientService.GetUser(update.UserId), update.UserFullInfo, true, false));
            }
        }



        public void Handle(UpdateBasicGroup update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeBasicGroup basic && basic.BasicGroupId == update.BasicGroup.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateBasicGroup(chat, update.BasicGroup));
            }
        }

        public void Handle(UpdateBasicGroupFullInfo update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeBasicGroup basic && basic.BasicGroupId == update.BasicGroupId)
            {
                BeginOnUIThread(() => Delegate?.UpdateBasicGroupFullInfo(chat, ClientService.GetBasicGroup(update.BasicGroupId), update.BasicGroupFullInfo));
            }
        }



        public void Handle(UpdateSupergroup update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup super && super.SupergroupId == update.Supergroup.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateSupergroup(chat, update.Supergroup));
            }
        }

        public void Handle(UpdateSupergroupFullInfo update)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup super && super.SupergroupId == update.SupergroupId)
            {
                BeginOnUIThread(() => Delegate?.UpdateSupergroupFullInfo(chat, ClientService.GetSupergroup(update.SupergroupId), update.SupergroupFullInfo));
            }
        }



        public void Handle(UpdateChatTitle update)
        {
            if (update.ChatId == _chat?.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateChatTitle(_chat));
            }
        }

        public void Handle(UpdateChatPhoto update)
        {
            if (update.ChatId == _chat?.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateChatPhoto(_chat));
            }
        }

        public void Handle(UpdateUserStatus update)
        {
            if (_chat?.Type is ChatTypePrivate privata && privata.UserId == update.UserId || _chat?.Type is ChatTypeSecret secret && secret.UserId == update.UserId)
            {
                BeginOnUIThread(() => Delegate?.UpdateUserStatus(_chat, ClientService.GetUser(update.UserId)));
            }
        }

        public void Handle(UpdateChatNotificationSettings update)
        {
            if (update.ChatId == _chat?.Id)
            {
                BeginOnUIThread(() => Delegate?.UpdateChatNotificationSettings(_chat));
            }
        }

        public void SendMessage()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var last = NavigationService.Frame.BackStack.LastOrDefault();
            if (last?.SourcePageType == typeof(ChatPage) && NavigationService.TryGetPeerFromParameter(last.Parameter, out long chatId))
            {
                if (chat.Id == chatId)
                {
                    NavigationService.GoBack();
                }
                else
                {
                    NavigationService.NavigateToChat(chat);
                }
            }
            else
            {
                NavigationService.NavigateToChat(chat);
            }
        }

        public void OpenStatistics()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(ChatStatisticsPage), chat.Id);

            //var fullInfo = ClientService.GetSupergroupFull(chat);
            //if (fullInfo == null || !fullInfo.CanViewStatistics)
            //{
            //    return;
            //}

            //var response = await ClientService.SendAsync(new GetChatStatisticsUrl(chat.Id, string.Empty));
            //if (response is ChatStatisticsUrl url)
            //{
            //    await Launcher.LaunchUriAsync(new Uri(url.Url));
            //}
        }

        public async void Block()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var confirm = await ShowPopupAsync(Strings.AreYouSureBlockContact, Strings.AppName, Strings.OK, Strings.Cancel);
            if (confirm != ContentDialogResult.Primary)
            {
                return;
            }

            ToggleIsBlocked(chat, true);
        }

        public async void Unblock()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var confirm = await ShowPopupAsync(Strings.AreYouSureUnblockContact, Strings.AppName, Strings.OK, Strings.Cancel);
            if (confirm != ContentDialogResult.Primary)
            {
                return;
            }

            ToggleIsBlocked(chat, false);
        }

        private void ToggleIsBlocked(Chat chat, bool blocked)
        {
            if (chat.Type is ChatTypePrivate privata)
            {
                ClientService.Send(new ToggleMessageSenderIsBlocked(new MessageSenderUser(privata.UserId), blocked));
            }
            else if (chat.Type is ChatTypeSecret secret)
            {
                ClientService.Send(new ToggleMessageSenderIsBlocked(new MessageSenderUser(secret.UserId), blocked));
            }
        }

        public async void Share()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate or ChatTypeSecret)
            {
                var user = ClientService.GetUser(chat.Type is ChatTypePrivate privata ? privata.UserId : chat.Type is ChatTypeSecret secret ? secret.UserId : 0);
                if (user != null)
                {
                    await ShowPopupAsync(typeof(ChooseChatsPopup), new ChooseChatsConfigurationPostMessage(new InputMessageContact(new Contact(user.PhoneNumber, user.FirstName, user.LastName, string.Empty, user.Id))));
                }
            }
        }

        public void CopyPhone()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var user = ClientService.GetUser(chat);
            if (user == null)
            {
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText($"+{user.PhoneNumber}");
            ClipboardEx.TrySetContent(dataPackage);
        }

        public void CopyDescription()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup super)
            {
                var supergroup = ClientService.GetSupergroupFull(super.SupergroupId);
                if (supergroup == null)
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText(supergroup.Description);
                ClipboardEx.TrySetContent(dataPackage);
            }
            else
            {
                var user = ClientService.GetUserFull(chat);
                if (user == null)
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText(user.BotInfo?.ShortDescription ?? user.Bio.Text);
                ClipboardEx.TrySetContent(dataPackage);
            }
        }

        public void CopyUsername()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup super)
            {
                var supergroup = ClientService.GetSupergroup(super.SupergroupId);
                if (supergroup == null || !supergroup.HasActiveUsername(out string username))
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText($"@{username}");
                ClipboardEx.TrySetContent(dataPackage);
            }
            else
            {
                var user = ClientService.GetUser(chat);
                if (user == null || !user.HasActiveUsername(out string username))
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText($"@{username}");
                ClipboardEx.TrySetContent(dataPackage);
            }
        }

        public void CopyUsernameLink()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup super)
            {
                var supergroup = ClientService.GetSupergroup(super.SupergroupId);
                if (supergroup == null || !supergroup.HasActiveUsername(out string username))
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText(MeUrlPrefixConverter.Convert(ClientService, username));
                ClipboardEx.TrySetContent(dataPackage);
            }
            else
            {
                var user = ClientService.GetUser(chat);
                if (user == null || !user.HasActiveUsername(out string username))
                {
                    return;
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText(MeUrlPrefixConverter.Convert(ClientService, username));
                ClipboardEx.TrySetContent(dataPackage);
            }
        }

        public async void GiftPremium()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (ClientService.TryGetUser(chat, out User user)
                && ClientService.TryGetUserFull(chat, out UserFullInfo userFull))
            {
                await ShowPopupAsync(new GiftPopup(ClientService, NavigationService, user, userFull.PremiumGiftOptions));
            }
        }

        public async void CreateSecretChat()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var confirm = await ShowPopupAsync(Strings.AreYouSureSecretChat, Strings.AreYouSureSecretChatTitle, Strings.Start, Strings.Cancel);
            if (confirm != ContentDialogResult.Primary)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata)
            {
                var response = await ClientService.SendAsync(new CreateNewSecretChat(privata.UserId));
                if (response is Chat result)
                {
                    NavigationService.NavigateToChat(result);
                }
            }
        }

        public async void ShowIdenticon()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            await ShowPopupAsync(new IdenticonPopup(SessionId, chat));
        }

        public async void Invite()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate or ChatTypeSecret)
            {
                var user = ClientService.GetUser(chat);
                if (user == null || user.Type is not UserTypeBot)
                {
                    return;
                }

                await ShowPopupAsync(typeof(ChooseChatsPopup), new ChooseChatsConfigurationStartBot(user));
            }
            else
            {
                var header = chat.Type is ChatTypeSupergroup supergroup && supergroup.IsChannel
                    ? Strings.AddSubscriber
                    : Strings.AddMember;

                var selected = await ChooseChatsPopup.PickUsersAsync(ClientService, header);
                if (selected == null || selected.Count == 0)
                {
                    return;
                }

                string title = Locale.Declension(Strings.R.AddManyMembersAlertTitle, selected.Count);
                string message;

                if (selected.Count <= 5)
                {
                    var names = string.Join(", ", selected.Select(x => x.FullName()));
                    message = string.Format(Strings.AddMembersAlertNamesText, names, chat.Title);
                }
                else
                {
                    message = Locale.Declension(Strings.R.AddManyMembersAlertNamesText, selected.Count, chat.Title);
                }

                var confirm = await ShowPopupAsync(message, title, Strings.Add, Strings.Cancel);
                if (confirm != ContentDialogResult.Primary)
                {
                    return;
                }

                var response = await ClientService.SendAsync(new AddChatMembers(chat.Id, selected.Select(x => x.Id).ToArray()));
                if (response is Error error)
                {

                }
            }
        }

        public void Mute()
        {
            ToggleMute(false);
        }

        public void Unmute()
        {
            ToggleMute(true);
        }

        public void ToggleMute()
        {
            ToggleMute(ClientService.Notifications.GetMutedFor(_chat) > 0);
        }

        private void ToggleMute(bool unmute)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            _notificationsService.SetMuteFor(chat, ClientService.Notifications.GetMutedFor(chat) > 0 ? 0 : 632053052);
        }

        #region Search

        public void Search()
        {
            OpenSearch(string.Empty);
        }

        public void OpenSearch(string query)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var last = NavigationService.Frame.BackStack.LastOrDefault();
            if (last?.SourcePageType == typeof(ChatPage) && NavigationService.TryGetPeerFromParameter(last.Parameter, out long chatId))
            {
                if (chat.Id == chatId)
                {
                    NavigationService.GoBack(new NavigationState { { "search", query } });
                }
            }
            else
            {
                NavigationService.NavigateToChat(chat, state: new NavigationState { { "search", query } });
            }
        }

        #endregion

        #region Call

        public void VoiceCall()
        {
            Call(false);
        }

        public void VideoCall()
        {
            Call(true);
        }

        private async void Call(bool video)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate or ChatTypeSecret)
            {
                _voipService.Start(chat.Id, video);
            }
            else if (chat.VideoChat.GroupCallId == 0)
            {
                await _voipGroupService.CreateAsync(chat.Id);
            }
            else
            {
                await _voipGroupService.JoinAsync(chat.Id);
            }
        }

        #endregion

        public void AddToContacts()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var user = ClientService.GetUser(chat);
            if (user == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(UserEditPage), user.Id);
        }

        public async void Edit()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (_topic != null)
            {
                var popup = new SupergroupTopicPopup(ClientService, _topic);

                var confirm = await ShowPopupAsync(popup);
                if (confirm == ContentDialogResult.Primary)
                {
                    ClientService.Send(new EditForumTopic(chat.Id, _topic.MessageThreadId, popup.Name, true, popup.SelectedEmojiId));
                }
            }
            else if (chat.Type is ChatTypeSupergroup or ChatTypeBasicGroup)
            {
                NavigationService.Navigate(typeof(SupergroupEditPage), chat.Id);
            }
            else if (chat.Type is ChatTypePrivate or ChatTypeSecret)
            {
                AddToContacts();
            }
        }

        public void Discuss()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypeSupergroup)
            {
                var fullInfo = ClientService.GetSupergroupFull(chat);
                if (fullInfo == null)
                {
                    return;
                }

                NavigationService.NavigateToChat(fullInfo.LinkedChatId);
            }
        }

        public void Join()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            ClientService.Send(new JoinChat(chat.Id));
        }

        public async void Delete()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            var message = Strings.AreYouSureDeleteAndExit;
            if (chat.Type is ChatTypePrivate or ChatTypeSecret)
            {
                message = Strings.AreYouSureDeleteContact;
            }
            else if (chat.Type is ChatTypeSupergroup super)
            {
                message = super.IsChannel ? Strings.ChannelLeaveAlert : Strings.MegaLeaveAlert;
            }

            var confirm = await ShowPopupAsync(message, Strings.AppName, Strings.OK, Strings.Cancel);
            if (confirm == ContentDialogResult.Primary)
            {
                if (chat.Type is ChatTypePrivate privata)
                {
                    ClientService.Send(new RemoveContacts(new[] { privata.UserId }));
                }
                else if (chat.Type is ChatTypeSecret secret)
                {
                    ClientService.Send(new RemoveContacts(new[] { secret.UserId }));
                }
                else
                {
                    if (chat.Type is ChatTypeBasicGroup or ChatTypeSupergroup)
                    {
                        await ClientService.SendAsync(new LeaveChat(chat.Id));
                    }

                    ClientService.Send(new DeleteChatHistory(chat.Id, true, false));
                }
            }

            //var user = _item as TLUser;
            //if (user == null)
            //{
            //    return;
            //}

            //var confirm = await ShowPopupAsync(Strings.AreYouSureDeleteContact, Strings.AppName, Strings.OK, Strings.Cancel);
            //if (confirm != ContentDialogResult.Primary)
            //{
            //    return;
            //}

            //var response = await LegacyService.DeleteContactAsync(user.ToInputUser());
            //if (response.IsSucceeded)
            //{
            //    // TODO: delete from synced contacts

            //    Aggregator.Publish(new TLUpdateContactLink
            //    {
            //        UserId = response.Result.User.Id,
            //        MyLink = response.Result.MyLink,
            //        ForeignLink = response.Result.ForeignLink
            //    });

            //    user.RaisePropertyChanged(() => user.HasFirstName);
            //    user.RaisePropertyChanged(() => user.HasLastName);
            //    user.RaisePropertyChanged(() => user.FirstName);
            //    user.RaisePropertyChanged(() => user.LastName);
            //    user.RaisePropertyChanged(() => user.FullName);
            //    user.RaisePropertyChanged(() => user.DisplayName);

            //    user.RaisePropertyChanged(() => user.HasPhone);
            //    user.RaisePropertyChanged(() => user.Phone);

            //    RaisePropertyChanged(() => IsEditEnabled);
            //    RaisePropertyChanged(() => IsAddEnabled);

            //    var dialog = ClientService.GetDialog(_item.ToPeer());
            //    if (dialog != null)
            //    {
            //        dialog.RaisePropertyChanged(() => dialog.With);
            //    }
            //}
        }

        #region Mute for

        public async void MuteFor(int? value)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (value is int update)
            {
                _notificationsService.SetMuteFor(chat, update);
            }
            else
            {
                var mutedFor = Settings.Notifications.GetMutedFor(chat);
                var popup = new ChatMutePopup(mutedFor);

                var confirm = await ShowPopupAsync(popup);
                if (confirm != ContentDialogResult.Primary)
                {
                    return;
                }

                if (mutedFor != popup.Value)
                {
                    _notificationsService.SetMuteFor(chat, popup.Value);
                }
            }
        }

        #endregion

        #region Set timer

        public RelayCommand<int?> SetTimerCommand { get; }
        private async void SetTimer(int? ttl)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            if (ttl is int value)
            {
                ClientService.Send(new SetChatMessageAutoDeleteTime(chat.Id, value));
            }
            else
            {
                var dialog = new ChatTtlPopup(chat.Type is ChatTypeSecret ? ChatTtlType.Secret : ChatTtlType.Normal);
                dialog.Value = chat.MessageAutoDeleteTime;

                var confirm = await ShowPopupAsync(dialog);
                if (confirm != ContentDialogResult.Primary)
                {
                    return;
                }

                ClientService.Send(new SetChatMessageAutoDeleteTime(chat.Id, dialog.Value));
            }
        }

        #endregion

        #region Supergroup

        public void OpenAdmins()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupAdministratorsPage), chat.Id);
        }

        public void OpenBanned()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupBannedPage), chat.Id);
        }

        public void OpenKicked()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupPermissionsPage), chat.Id);
        }

        public void OpenMembers()
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupMembersPage), chat.Id);
        }

        public virtual ChatMemberCollection CreateMembers(long supergroupId)
        {
            return new ChatMemberCollection(ClientService, supergroupId, new SupergroupMembersFilterRecent());
        }

        #endregion

        #region Context menu

        public void PromoteMember(ChatMember member)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupEditAdministratorPage), state: NavigationState.GetChatMember(chat.Id, member.MemberId));
        }

        public void RestrictMember(ChatMember member)
        {
            var chat = _chat;
            if (chat == null)
            {
                return;
            }

            NavigationService.Navigate(typeof(SupergroupEditRestrictedPage), state: NavigationState.GetChatMember(chat.Id, member.MemberId));
        }

        public async void RemoveMember(ChatMember member)
        {
            var chat = _chat;
            if (chat == null || _members == null)
            {
                return;
            }

            var index = _members.IndexOf(member);

            _members.Remove(member);

            var response = await ClientService.SendAsync(new SetChatMemberStatus(chat.Id, member.MemberId, new ChatMemberStatusBanned()));
            if (response is Error)
            {
                _members.Insert(index, member);
            }
        }

        #endregion

    }

    public class ChatMemberCollection : LegacyIncrementalCollection<ChatMember>
    {
        private readonly IClientService _clientService;
        private readonly long _chatId;
        private readonly ChatMembersFilter _filter2;
        private readonly string _query;

        private readonly long _supergroupId;
        private readonly SupergroupMembersFilter _filter;

        private bool _hasMore;

        public ChatMemberCollection(IClientService clientService, long chatId, string query, ChatMembersFilter filter)
        {
            _clientService = clientService;
            _chatId = chatId;
            _filter2 = filter;
            _query = query;
            _hasMore = true;
        }

        public ChatMemberCollection(IClientService clientService, long supergroupId, SupergroupMembersFilter filter)
        {
            _clientService = clientService;
            _supergroupId = supergroupId;
            _filter = filter;
            _hasMore = true;
        }

        public override async Task<IList<ChatMember>> LoadDataAsync()
        {
            if (_chatId != 0)
            {
                var response = await _clientService.SendAsync(new SearchChatMembers(_chatId, _query, 200, _filter2));
                if (response is ChatMembers members)
                {
                    _hasMore = false;

                    if (_filter2 is null or ChatMembersFilterMembers)
                    {
                        return members.Members.OrderBy(x => x, new ChatMemberComparer(_clientService, true)).ToArray();
                    }

                    return members.Members;
                }
            }
            else
            {
                var response = await _clientService.SendAsync(new GetSupergroupMembers(_supergroupId, _filter, Count, 200));
                if (response is ChatMembers members)
                {
                    if (members.Members.Count < 200)
                    {
                        _hasMore = false;
                    }

                    if ((_filter == null || _filter is SupergroupMembersFilterRecent) && Count == 0 && members.TotalCount <= 200)
                    {
                        return members.Members.OrderBy(x => x, new ChatMemberComparer(_clientService, true)).ToArray();
                    }

                    return members.Members;
                }
            }

            return new ChatMember[0];
        }

        protected override bool GetHasMoreItems()
        {
            return _hasMore;
        }
    }

    public class ChatMemberGroupedCollection : LegacyIncrementalCollection<object>
    {
        private readonly IClientService _clientService;
        private readonly long _chatId;
        private readonly string _query;

        private readonly long _supergroupId;
        private SupergroupMembersFilter _filter;
        private int _offset;

        private readonly bool _group;

        private bool _hasMore;

        public ChatMemberGroupedCollection(IClientService clientService, long chatId, string query, bool group)
        {
            _clientService = clientService;
            _chatId = chatId;
            _query = query;
            _hasMore = true;
            _group = group;
        }

        public ChatMemberGroupedCollection(IClientService clientService, long supergroupId, bool group)
        {
            _clientService = clientService;
            _supergroupId = supergroupId;
            _filter = group ? new SupergroupMembersFilterContacts() : null;
            _hasMore = true;
            _group = group;
        }

        public override async Task<IList<object>> LoadDataAsync()
        {
            if (_chatId != 0)
            {
                var response = await _clientService.SendAsync(new SearchChatMembers(_chatId, _query, 200, null));
                if (response is ChatMembers members)
                {
                    _hasMore = false;

                    return members.Members.OrderBy(x => x, new ChatMemberComparer(_clientService, true)).ToArray();
                }
            }
            else
            {
                if (_group)
                {

                    var response = await _clientService.SendAsync(new GetSupergroupMembers(_supergroupId, _filter, _offset, 200));
                    if (response is ChatMembers members)
                    {

                        List<ChatMember> items;
                        if ((_filter == null || _filter is SupergroupMembersFilterRecent) && _offset == 0 && members.TotalCount <= 200)
                        {
                            items = members.Members.OrderBy(x => x, new ChatMemberComparer(_clientService, true)).ToList();
                        }
                        else
                        {
                            items = members.Members.ToList();
                        }

                        for (int i = 0; i < items.Count; i++)
                        {
                            var already = this.OfType<ChatMember>().FirstOrDefault(x => x.MemberId.AreTheSame(items[i].MemberId));
                            if (already != null)
                            {
                                items.RemoveAt(i);
                                i--;
                            }
                        }

                        string title = null;
                        if (_offset == 0)
                        {
                            switch (_filter)
                            {
                                case SupergroupMembersFilterContacts contacts:
                                    title = Strings.GroupContacts;
                                    break;
                                case SupergroupMembersFilterBots bots:
                                    title = Strings.ChannelBots;
                                    break;
                                case SupergroupMembersFilterAdministrators administrators:
                                    title = Strings.ChannelAdministrators;
                                    break;
                                case SupergroupMembersFilterRecent recent:
                                    title = Strings.ChannelOtherMembers;
                                    break;
                            }
                        }



                        _offset += members.Members.Count;

                        if (members.Members.Count < 200)
                        {
                            switch (_filter)
                            {
                                case SupergroupMembersFilterContacts contacts:
                                    _filter = new SupergroupMembersFilterBots();
                                    _offset = 0;
                                    break;
                                case SupergroupMembersFilterBots bots:
                                    _filter = new SupergroupMembersFilterAdministrators();
                                    _offset = 0;
                                    break;
                                case SupergroupMembersFilterAdministrators administrators:
                                    _filter = new SupergroupMembersFilterRecent();
                                    _offset = 0;
                                    break;
                                case SupergroupMembersFilterRecent recent:
                                    _hasMore = false;
                                    break;
                            }
                        }

                        if (title != null && items.Count > 0)
                        {
                            return new object[] { title }.Union(items).ToArray();
                        }
                        else
                        {
                            return items.Cast<object>().ToArray();
                        }
                    }
                }
                else
                {
                    var response = await _clientService.SendAsync(new GetSupergroupMembers(_supergroupId, _filter, Count, 200));
                    if (response is ChatMembers members)
                    {
                        if (members.Members.Count < 200)
                        {
                            _hasMore = false;
                        }

                        if ((_filter == null || _filter is SupergroupMembersFilterRecent) && Count == 0 && members.TotalCount <= 200)
                        {
                            return members.Members.OrderBy(x => x, new ChatMemberComparer(_clientService, true)).ToArray();
                        }

                        return members.Members.Cast<object>().ToArray();
                    }
                }
            }

            return new ChatMember[0];
        }

        protected override bool GetHasMoreItems()
        {
            return _hasMore;
        }
    }

    public class ChatMemberComparer : IComparer<ChatMember>
    {
        private readonly IClientService _clientService;
        private readonly bool _epoch;

        public ChatMemberComparer(IClientService clientService, bool epoch)
        {
            _clientService = clientService;
            _epoch = epoch;
        }

        public int Compare(ChatMember x, ChatMember y)
        {
            _clientService.TryGetUser(x.MemberId, out User xUser);
            _clientService.TryGetUser(y.MemberId, out User yUser);

            if (xUser == null || yUser == null)
            {
                return -1;
            }

            if (_epoch)
            {
                var epoch = LastSeenConverter.GetIndex(yUser).CompareTo(LastSeenConverter.GetIndex(xUser));
                if (epoch == 0)
                {
                    var fullName = xUser.FirstName.CompareTo(yUser.FirstName);
                    if (fullName == 0)
                    {
                        return yUser.Id.CompareTo(xUser.Id);
                    }

                    return fullName;
                }

                return epoch;
            }
            else
            {
                var fullName = xUser.FirstName.CompareTo(yUser.FirstName);
                if (fullName == 0)
                {
                    return yUser.Id.CompareTo(xUser.Id);
                }

                return fullName;
            }
        }
    }
}
