using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PropertyChanged;

namespace Saradomin.View.Controls
{
    [DoNotNotify]
    public partial class SingleplayerView : UserControl
    {
        private TextBox _wiredLogTextBox;

        public SingleplayerView()
        {
            InitializeComponent();

            // The log TextBox is supplied by the view model as the
            // ScrollViewer content. Wire it after bindings have populated the
            // content, then keep the newest update/download line in view.
            AttachedToVisualTree += (_, _) =>
                Dispatcher.UIThread.Post(WireLogAutoScroll, DispatcherPriority.Background);
            DataContextChanged += (_, _) =>
                Dispatcher.UIThread.Post(WireLogAutoScroll, DispatcherPriority.Background);
        }

        private void WireLogAutoScroll()
        {
            var viewer = this.FindControl<ScrollViewer>("SingleplayerLogScrollViewer");
            if (viewer == null)
                return;

            var logTextBox = viewer.Content as TextBox;
            if (ReferenceEquals(logTextBox, _wiredLogTextBox))
                return;

            if (_wiredLogTextBox != null)
                _wiredLogTextBox.TextChanged -= OnLogTextChanged;

            _wiredLogTextBox = logTextBox;

            if (_wiredLogTextBox != null)
            {
                _wiredLogTextBox.TextChanged += OnLogTextChanged;
                Dispatcher.UIThread.Post(viewer.ScrollToEnd, DispatcherPriority.Background);
            }
        }

        private void OnLogTextChanged(object sender, TextChangedEventArgs e)
        {
            // Text layout/extent updates after TextChanged, so defer the
            // scroll one dispatcher turn. This keeps the newest progress line
            // visible even when several update messages arrive quickly.
            Dispatcher.UIThread.Post(() =>
            {
                var viewer = this.FindControl<ScrollViewer>("SingleplayerLogScrollViewer");
                viewer?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
    }
}
