//
// Copyright Fela Ameghino 2015-2023
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using Telegram.Services;
using Telegram.Services.Factories;

namespace Telegram.ViewModels
{
    public class DialogPinnedViewModel : DialogViewModel
    {
        public DialogPinnedViewModel(IClientService clientService, ISettingsService settingsService, IEventAggregator aggregator, ILocationService locationService, INotificationsService pushService, IPlaybackService playbackService, IVoipService voipService, IVoipGroupService voipGroupService, INetworkService networkService, IStorageService storageService, ITranslateService translateService, IMessageFactory messageFactory)
            : base(clientService, settingsService, aggregator, locationService, pushService, playbackService, voipService, voipGroupService, networkService, storageService, translateService, messageFactory)
        {
        }

        public override DialogType Type => DialogType.Pinned;
    }
}
