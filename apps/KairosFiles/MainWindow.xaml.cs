using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KairosFiles
{
    public partial class MainWindow : Window
    {
        private readonly FileBrowser _browser = new();
        private List<FileItem> _current = new();

        // Para copiar/mover: ruta pendiente y si es mover.
        private string? _clipboardPath;
        private bool _clipboardIsMove;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            EnableAcrylic(helper.Handle);
            BuildSidebar();

            // Empezar en la carpeta del usuario.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            NavigateTo(home);
        }

        // ---------- Acrylic ----------
        private void EnableAcrylic(IntPtr hwnd)
        {
            int corner = Interop.DWMWCP_ROUND;
            Interop.DwmSetWindowAttribute(hwnd, Interop.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            var accent = new Interop.AccentPolicy
            {
                AccentState = Interop.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0x88221414
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Interop.WindowCompositionAttributeData
            {
                Attribute = Interop.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            Interop.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(ptr);

            // Permitir arrastrar la ventana desde la barra superior.
            MouseLeftButtonDown += (_, args) =>
            {
                if (args.ButtonState == MouseButtonState.Pressed && args.GetPosition(this).Y < 64)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        // ---------- Sidebar de accesos rapidos ----------
        private void BuildSidebar()
        {
            var places = new (string label, string glyph, Environment.SpecialFolder folder)[]
            {
                ("Inicio", "\uE80F", Environment.SpecialFolder.UserProfile),
                ("Escritorio", "\uE7F4", Environment.SpecialFolder.Desktop),
                ("Documentos", "\uE8A5", Environment.SpecialFolder.MyDocuments),
                ("Descargas", "\uE896", Environment.SpecialFolder.UserProfile), // ajustado abajo
                ("Imagenes", "\uEB9F", Environment.SpecialFolder.MyPictures),
                ("Musica", "\uE8D6", Environment.SpecialFolder.MyMusic),
                ("Videos", "\uE714", Environment.SpecialFolder.MyVideos),
            };

            foreach (var p in places)
            {
                string path;
                if (p.label == "Descargas")
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                else
                    path = Environment.GetFolderPath(p.folder);

                Sidebar.Children.Add(MakeSidebarButton(p.label, p.glyph, path));
            }

            // Unidades (C:, D:...)
            Sidebar.Children.Add(new TextBlock
            {
                Text = "Este equipo",
                Foreground = (Brush)FindResource("TextMuted"),
                FontFamily = new FontFamily("Inter, Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 14, 0, 6)
            });

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    Sidebar.Children.Add(MakeSidebarButton(
                        $"{drive.Name.TrimEnd('\\')}", "\uEDA2", drive.RootDirectory.FullName));
                }
            }
            catch { }
        }

        private Button MakeSidebarButton(string label, string glyph, string path)
        {
            var btn = new Button
            {
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 1, 0, 1),
                Tag = path,
                Height = 36
            };

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "B";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(10, 0, 0, 0));

            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            sp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetValue(TextBlock.TextProperty, glyph);
            icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
            icon.SetValue(TextBlock.FontSizeProperty, 15.0);
            icon.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(0xCC, 0xA7, 0x8B, 0xFA)));
            icon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 12, 0));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextProperty, label);
            text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Inter, Segoe UI"));
            text.SetValue(TextBlock.FontSizeProperty, 13.0);
            text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(0xE6, 0xF4, 0xF4, 0xFF)));
            text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            sp.AppendChild(icon);
            sp.AppendChild(text);
            border.AppendChild(sp);
            template.VisualTree = border;

            var trigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x1F, 0x5B, 0x5B, 0xF5)), "B"));
            template.Triggers.Add(trigger);

            btn.Template = template;
            btn.Click += (_, _) => { if (btn.Tag is string p) NavigateTo(p); };
            return btn;
        }

        // ---------- Navegacion ----------
        private void NavigateTo(string path)
        {
            _current = _browser.Navigate(path);
            ApplyList();
        }

        private void ApplyList()
        {
            var filter = SearchBox?.Text ?? "";
            var view = _browser.Filter(_current, filter);
            FileList.ItemsSource = null;
            FileList.ItemsSource = view;
            PathBox.Text = _browser.CurrentPath;
            BackBtn.IsEnabled = _browser.CanGoBack;
            FwdBtn.IsEnabled = _browser.CanGoForward;
        }

        private void Back_Click(object sender, RoutedEventArgs e) { _current = _browser.GoBack(); ApplyList(); }
        private void Forward_Click(object sender, RoutedEventArgs e) { _current = _browser.GoForward(); ApplyList(); }
        private void Up_Click(object sender, RoutedEventArgs e) { _current = _browser.GoUp(); ApplyList(); }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var p = PathBox.Text.Trim();
                if (Directory.Exists(p)) NavigateTo(p);
                else MessageBox.Show("La ruta no existe.", "KairosFiles");
                e.Handled = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyList();

        // ---------- Abrir ----------
        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void FileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Back) { _current = _browser.GoBack(); ApplyList(); e.Handled = true; }
        }

        private void OpenSelected()
        {
            if (FileList.SelectedItem is not FileItem item) return;
            if (item.IsDirectory)
            {
                NavigateTo(item.FullPath);
            }
            else
            {
                try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "KairosFiles"); }
            }
        }

        // ---------- Menu contextual ----------
        private void Ctx_Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void Ctx_Copy_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is FileItem item)
            {
                _clipboardPath = item.FullPath;
                _clipboardIsMove = false;
            }
        }

        private void Ctx_Move_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is FileItem item)
            {
                _clipboardPath = item.FullPath;
                _clipboardIsMove = true;
            }
        }

        private void Ctx_Paste_Click(object sender, RoutedEventArgs e) => PasteHere();

        // Pega/mueve lo marcado a la carpeta actual.
        private void PasteHere()
        {
            if (string.IsNullOrEmpty(_clipboardPath))
            {
                MessageBox.Show("No hay nada copiado o cortado.", "KairosFiles");
                return;
            }
            var dest = _browser.CurrentPath;
            var (ok, err) = _clipboardIsMove
                ? FileOps.MoveTo(_clipboardPath, dest)
                : FileOps.CopyTo(_clipboardPath, dest);

            if (!ok) MessageBox.Show($"Error: {err}", "KairosFiles");
            if (_clipboardIsMove) _clipboardPath = null; // tras mover, ya no esta en origen
            _current = _browser.Refresh();
            ApplyList();
        }

        private void Ctx_Rename_Click(object sender, RoutedEventArgs e) => RenameSelected();

        private void RenameSelected()
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var dialog = new RenameDialog(item.Name) { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var (ok, err) = FileOps.Rename(item.FullPath, dialog.NewName.Trim());
                if (!ok) MessageBox.Show($"Error: {err}", "KairosFiles");
                _current = _browser.Refresh();
                ApplyList();
            }
        }

        private void Ctx_Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void DeleteSelected()
        {
            if (FileList.SelectedItem is not FileItem item) return;
            var r = MessageBox.Show($"Enviar a la papelera:\n{item.Name}?", "KairosFiles",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var (ok, err) = FileOps.Delete(item.FullPath);
            if (!ok) MessageBox.Show($"Error: {err}", "KairosFiles");
            _current = _browser.Refresh();
            ApplyList();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
