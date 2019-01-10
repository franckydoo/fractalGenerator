using System.Collections.Generic;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaFractalGenerator
{
    public class MainWindow : Window
    {
        private MandelBrotModel _viewModel;
        private IControl _img;

        public MainWindow()
        {
            InitializeComponent();
            this.AttachDevTools();

            DataContext = _viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoaderPortableXaml.Load(this);
            _img = ((Grid) Content).Children.First();
            _img.PointerMoved += Image_PointerMoved;
            _img.PointerPressed += Img_PointerPressed;
            _img.PointerReleased += Img_PointerReleased;
            _viewModel = new MandelBrotModel(() =>
                Dispatcher.UIThread.InvokeAsync(() => _img.InvalidateVisual()).Wait());
        }

        private void Image_PointerMoved(object sender, PointerEventArgs e)
        {
            if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
            {
                var (x, y) = GetScaledPosition(e, _img);
                _viewModel.Rectangle(x, y);
            }
        }

        private void Img_PointerReleased(object sender, PointerEventArgs e)
        {
            if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
            {
                var (x, y) = GetScaledPosition(e, _img);
                _viewModel.RectangleZoom(x, y);
            }
        }

        private async void Img_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.MouseButton == MouseButton.Left && e.ClickCount == 1)
            {
                var (x, y) = GetScaledPosition(e, _img);
                _viewModel.RectangleInit(x, y);
                _viewModel.CenterBitmap(x, y);
            }
            if (e.MouseButton == MouseButton.Right && e.ClickCount == 1)
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Save an Image File",
                    DefaultExtension = "png",
                    InitialFileName = "fractal.png",
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter {Name = "Pictures", Extensions = new List<string> {"png", "jpg", "bmp", "gif"}}
                    }
                };
                var result = await dlg.ShowAsync(this);
                if (result != null)
                {
                    _viewModel.StoreFile(result);
                }
            }
        }

        private static (double x, double y) GetScaledPosition(PointerEventArgs e, IVisual visual)
        {
            var pos = e.GetPosition(visual);

            var x = pos.X / visual.Bounds.Width;
            var y = pos.Y / visual.Bounds.Height;

            return (x, y);
        }
    }
}
