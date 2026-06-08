using System.Windows;
using System.Windows.Controls;

namespace MetaDataIAPlugin
{
    public partial class MetaDataIAPreviewWindow : UserControl
    {
        public MetaDataIAPreviewWindow(MetaDataIAPreviewViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Apply_OnClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void Cancel_OnClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
