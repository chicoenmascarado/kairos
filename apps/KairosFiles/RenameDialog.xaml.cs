using System.Windows;
using System.Windows.Input;

namespace KairosFiles
{
    public partial class RenameDialog : Window
    {
        public string NewName => NameInput.Text;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            NameInput.Text = currentName;
            Loaded += (_, _) =>
            {
                NameInput.Focus();
                // Seleccionar el nombre sin la extension, como hace Windows.
                var dot = currentName.LastIndexOf('.');
                if (dot > 0) NameInput.Select(0, dot);
                else NameInput.SelectAll();
            };
        }

        private void NameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { DialogResult = true; Close(); }
            else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
