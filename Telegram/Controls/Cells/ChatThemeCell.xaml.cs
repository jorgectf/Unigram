//
// Copyright Fela Ameghino 2015-2023
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//
using Telegram.Common;
using Telegram.Services;
using Telegram.Services.Settings;
using Telegram.ViewModels.Settings;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Telegram.Controls.Cells
{
    public sealed partial class ChatThemeCell : UserControl
    {
        private ChatThemeViewModel _theme;

        public ChatThemeCell()
        {
            InitializeComponent();
        }

        public void Update(ChatThemeViewModel theme)
        {
            _theme = theme;

            Name.Text = theme?.Name ?? string.Empty;

            var settings = ActualTheme == ElementTheme.Light ? theme.LightSettings : theme.DarkSettings;
            if (settings == null)
            {
                NoTheme.Visibility = Visibility.Visible;

                Preview.Visibility = Visibility.Collapsed;

                Outgoing.Fill = null;
                Incoming.Fill = null;
                return;
            }

            NoTheme.Visibility = Visibility.Collapsed;

            Preview.Visibility = Visibility.Visible;
            Preview.UpdateSource(theme.ClientService, settings.Background, true);

            Outgoing.Fill = settings.OutgoingMessageFill;
            Incoming.Fill = new SolidColorBrush(ThemeAccentInfo.Colorize(ActualTheme == ElementTheme.Light ? TelegramThemeType.Day : TelegramThemeType.Tinted, settings.AccentColor.ToColor(), "MessageBackgroundBrush"));
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_theme != null)
            {
                Update(_theme);
            }
        }
    }
}
