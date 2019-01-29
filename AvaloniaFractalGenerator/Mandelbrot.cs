using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace AvaloniaFractalGenerator {
    public class MandelBrotModel {
        private readonly Action _invalidate;
        private bool _isRectZoom = false;
        private bool _isCenter = false;
        private const int _resX = 1920;
        private const int _resY = 1080;
        private const int _maxZoomFactor = 4194304;
        private const int _maxAngle = 1000000;
        private const int _delayMs = 1;
        private class _pixelValue {
            public double X {get;set;}
            public double Y {get;set;}
            public double Iterations {get;set;} 
        }
        private List<_pixelValue> _calculatedValues {get; set;}
        private double _oldDetails {get; set;}
        private double _details {get; set;}
        private double _addX {get; set;}
        private double _addY {get; set;}
        private double _offsetX {get; set;}
        private double _offsetY {get; set;}
        private Color[] _colors {get; set;}
        private int _zoomFactor {get; set;}
        private int[] _pixels {get; set;}
        private bool _refresh {get; set;}
        private const string _defaultMsg = "Move sliders to control detail level, box blur, color frequency and color phases.";
        
        public TextBlock MsgBox {get;set;}
        public Rectangle Rect {get;set;}
        public int FilterValue {get; set;}
        public int BlurValue {get; set;}
        public double FreqRed {get; set;}
        public double PhaseRed {get; set;}
        public double FreqGreen {get; set;}
        public double PhaseGreen {get; set;}
        public double FreqBlue {get; set;}
        public double PhaseBlue {get; set;}
        public WritableBitmap Bitmap {get;}
        public WritableBitmap Overlay {get;}  
        public ICommand ResetCommand {get;}
        public ICommand ZoomInCommand {get;}
        public ICommand ZoomOutCommand {get;}
        public ICommand CenterCommand {get;}
        public ICommand RectZoomCommand {get;}

        public MandelBrotModel(Action invalidate) {
            _invalidate = invalidate;

            ResetCommand = new DelegateCommand(Reset);
            ZoomInCommand = new DelegateCommand(ZoomIn);
            ZoomOutCommand = new DelegateCommand(ZoomOut);
            CenterCommand = new DelegateCommand(Center);
            RectZoomCommand = new DelegateCommand(RectZoom);

            Bitmap = new WritableBitmap(_resX, _resY, PixelFormat.Bgra8888);   
            Overlay = new WritableBitmap(_resX, _resY, PixelFormat.Bgra8888);   
  
            FilterValue = 50;
            BlurValue = 3;  
            FreqRed = 0.0015;
            PhaseRed = 0;
            FreqGreen = 0.0015;
            PhaseGreen = 1;
            FreqBlue = 0.0015;
            PhaseBlue = 0;

            _addX = -1.2395;
            _addY = 0.1;       
            _pixels = new int[_resX * _resY];
            _zoomFactor = 64;
            
            _details = FilterValue + _zoomFactor * 2;
            _refresh = true; 
        }
        public void Run() {
            Task.Run(() => TaskRun());
        }
        public void CenterBitmap(double x, double y) {
            if (_isCenter) {
                if (_refresh) return;
                Center(x,y);
                _refresh = true;
                _isCenter = false;
                MsgBox.Text = _defaultMsg;
            }
        }
        public void RectangleInit(double x, double y) {
            if (_isRectZoom) {
                (_offsetX, _offsetY) = (x, y);
            }
        }
        public void Rectangle(double x, double y) {
            if (_isRectZoom) {                
                Canvas.SetLeft(Rect, (x < _offsetX ? x : _offsetX));
                Canvas.SetTop(Rect, (y < _offsetY ? y : _offsetY));
                Rect.Width = (x > _offsetX ? x - _offsetX : _offsetX - x);
                Rect.Height = (y > _offsetY ? y - _offsetY : _offsetY - y);
            }
        }
        public void RectangleZoom(double wWidth, double wHeight) {
            if (_isRectZoom) {
                double left = Canvas.GetLeft(Rect) / wWidth,
                       top = Canvas.GetTop(Rect) / wHeight,
                       width = Rect.Width / wWidth,
                       height = Rect.Height / wHeight;
                int    xProz = (int)(100 * width),
                       yProz = (int)(100 * height);

                Canvas.SetLeft(Rect, -100);
                Canvas.SetTop(Rect, -100);
                Rect.Width = 1;
                Rect.Height = 1;
                _isRectZoom = false;
                if (xProz > 0 && yProz > 0) {
                    if (_refresh) return;
                    Center(left + (width / 2), top + (height / 2));
                    _zoomFactor = (int)_zoomFactor * (100 / (xProz > yProz ? xProz : yProz)); 
                    CheckMaxAndInitZoom();
                }
            }
        }
        public unsafe void StoreFile(string fileName) {
            MemoryStream data = new MemoryStream();
            Bitmap.Save(data);
            data.Seek(0, SeekOrigin.Begin);
            try {
                using (var img = SixLabors.ImageSharp.Image.Load(data, new PngDecoder())) {
                    img.Save(fileName);
                }
            } catch(Exception e) {
                Console.Write(e.Message);
            }
        }

        private void TaskRun() {
            while (true) {
                if (_refresh) {
                    RePaintMandelbrot();
                    _refresh = false;
                }
                _invalidate();
                Thread.Sleep(_delayMs);
            }
        }
        private unsafe void OverlayLoadingBar(int percent, Color? color = null, int left = 10, int top = 10, int size = 10) {
            Color c = color ?? Colors.Green;
            int width = Overlay.PixelWidth,
                height = Overlay.PixelHeight,   
                pixel = (int)(c.B + ((uint) c.G << 8) + ((uint) c.R << 16) + ((uint) c.A << 24));
    
            using (var buf = Overlay.Lock()) {
                for (int x = left; x <= left + 100 * size; x++) {
                    for (int y = top; y <= top + size; y++) {
                        if (x >= 0 && x < width && y >= 0 && y < height) {
                            uint* ptr = (uint*) buf.Address;
                            ptr += (uint)width * y + x;
                            *ptr = (x < (left + percent * size) ? (uint)pixel : 0);
                        }
                    }
                }
            }            
        }
        private unsafe void PutPixelPtr(int pos, int pixel) {   
            using (var buf = Bitmap.Lock()) {        
                uint* ptr = (uint*) buf.Address;
                ptr += (uint)pos;
                _pixels[pos] = (int)pixel;
                *ptr = (uint)pixel;
            }
        }
        private unsafe void ResetBitmap() {  
            _pixels = new int[_resX * _resY];
            using (var buf = Bitmap.Lock()) {
                uint* ptr = (uint*)buf.Address;
                int w = Bitmap.PixelWidth,
                    h = Bitmap.PixelHeight;

                for (var i = 0; i < w * h; i++) {
                    *(ptr + i) = 0;
                }
            }
        }
        private void PutPixel(double x, double y, Color c, int size = 1) {
            int width = Bitmap.PixelWidth,
                height = Bitmap.PixelHeight,
                px = (int) (x * width),
                py = (int) (y * height),
                pixel = (int)(c.B + ((uint) c.G << 8) + ((uint) c.R << 16) + ((uint) c.A << 24));
    
            for (int x0 = px - size; x0 <= px + size; x0++) {
                for (int y0 = py - size; y0 <= py + size; y0++) {
                    if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height) {
                        int pos = width * y0 + x0;
                        _pixels[pos] = pixel;
                    }
                }
            }
        }
        private void Reset() {
            if (_refresh) return;
            RepaintBitmap();
        }
        private void RepaintBitmap() {
            _colors = GenerateColors(1000000);
            ResetBitmap();
            _details = FilterValue + _zoomFactor * 2;
            if (_oldDetails != _details) _refresh = true;
            Paint();
            BoxBlur(Bitmap, BlurValue);
        }
        private void BoxBlur(WritableBitmap bmp, int range) {        
            BoxBlurHorizontal(bmp, range);
            BoxBlurVertical(bmp, range);
        }
        private void BoxBlurHorizontal(WritableBitmap bmp, int range) {
            int w = bmp.PixelWidth,
                h = bmp.PixelHeight,
                halfRange = range / 2,
                index = 0;
            int[] newColors = new int[w];
        
            for (int y = 0; y < h; y++) {
                int hits = 0,
                    r = 0,
                    g = 0,
                    b = 0;
                for (int x = -halfRange; x < w; x++) {
                    int oldPixel = x - halfRange - 1;
                    if (oldPixel >= 0) {
                        int col = _pixels[index + oldPixel];
                        if (col != 0) {
                            r -= ((byte)(col >> 16));
                            g -= ((byte)(col >> 8 ));
                            b -= ((byte)col);
                        }
                        hits--;
                    }
                    int newPixel = x + halfRange;
                    if (newPixel < w) {
                        int col = _pixels[index + newPixel];
                        if (col != 0) {
                            r += ((byte)(col >> 16));
                            g += ((byte)(col >> 8 ));
                            b += ((byte)col);
                        }
                        hits++;
                    }
                    if (x >= 0) {
                        int color =
                            (255 << 24)
                            | ((byte)(r / hits) << 16)
                            | ((byte)(g / hits) << 8 )
                            | ((byte)(b / hits));
                        newColors[x] = color;
                    }
                }
                for (int x = 0; x < w; x++) {
                    PutPixelPtr(index + x, newColors[x]);
                }        
                index += w;
            }
        }
        private void BoxBlurVertical(WritableBitmap bmp, int range) {
            int w = bmp.PixelWidth,
                h = bmp.PixelHeight,
                halfRange = range / 2,
                oldPixelOffset = -(halfRange + 1) * w,
                newPixelOffset = (halfRange) * w;        
            int[] newColors = new int[h];
        
            for (int x = 0; x < w; x++) {
                int hits = 0,
                    r = 0,
                    g = 0,
                    b = 0,
                    index = -halfRange * w + x;
                for (int y = -halfRange; y < h; y++) {
                    int oldPixel = y - halfRange - 1;
                    if (oldPixel >= 0) {
                        int col = _pixels[index + oldPixelOffset];
                        if (col != 0) {
                            r -= ((byte)(col >> 16));
                            g -= ((byte)(col >> 8 ));
                            b -= ((byte)col);
                        }
                        hits--;
                    }
                    int newPixel = y + halfRange;
                    if (newPixel < h) {
                        int col = _pixels[index + newPixelOffset];
                        if (col != 0) {
                            r += ((byte)(col >> 16));
                            g += ((byte)(col >> 8 ));
                            b += ((byte)col);
                        }
                        hits++;
                    }
                    if (y >= 0) {
                        int color =
                            (255 << 24)
                            | ((byte)(r / hits) << 16)
                            | ((byte)(g / hits) << 8 )
                            | ((byte)(b / hits));
                        newColors[y] = color;
                    }
                    index += w;
                }
                for (int y = 0; y < h; y++) {
                    PutPixelPtr(y * w + x, newColors[y]);
                }
            }
        }
        private Color[] GenerateColors(int number) {
            List<Color> colors = new List<Color>(number);
            double step = _maxAngle / number;
            for(int i = 0; i < number; ++i) {
                double r = (Math.Sin(FreqRed * i * step + PhaseRed) + 1) * .5,
                       g = (Math.Sin(FreqGreen * i * step + PhaseGreen) + 1) * .5,
                       b = (Math.Sin(FreqBlue * i * step + PhaseBlue) + 1) * .5;
                colors.Add(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            }
            return colors.ToArray();
        }
        private double Julia (double x, double y, double xAdd, double yAdd, double maxResultQuad, double max) {
            double remain = max,
                   xx = x * x, 
                   yy = y * y, 
                   xy = x * y, 
                   resultQuad = xx + yy;
            //for deeper zooms use Julia with decimal and buy a Supercomputer
            while(resultQuad <= maxResultQuad && remain-- > 0) {
                x  = xx - yy + xAdd;
                y  = xy + xy + yAdd;
                xx = x * x;
                yy = y * y;
                xy = x * y;
                resultQuad = xx + yy;
            }
            return max - remain - Math.Log(Math.Log(resultQuad) / Math.Log(4)) / Math.Log(2);
        }
        private void CalcMandelbrot(int width, int height, double maxResultQuad, double max) {
            _oldDetails = max; 
            int oldPercent = 0;
            for (int row = 0; row < height; row++) {
                for (int col = 0; col < width; col++) {
                    double x = (double)((col - width / 2.0) * 4.0 / width),
                           y = (double)((row - height / 2.0) * 4.0 / width), 
                           i = Julia(x / _zoomFactor + _addX, 
                               y / _zoomFactor + _addY, 
                               x / _zoomFactor + _addX, 
                               y / _zoomFactor + _addY, maxResultQuad, max);
                    if (i < max && i >= 0) { 
                        _calculatedValues.Add(new _pixelValue {
                            X = (double)col / width, 
                            Y = (double)row / height, 
                            Iterations = i
                        });                 
                    }   
                }        
                int percent = (((row + 1) * 100) / height);
                if (percent % 1 == 0 && percent != oldPercent) {
                    oldPercent = percent;
                    OverlayLoadingBar(percent < 100 ? percent : 0); 
                    _invalidate();
                }
            }
        }
        private void ZoomOut() {
            if (_refresh) return;
            _zoomFactor /= 2;
            if (_zoomFactor < 1) _zoomFactor = 1;
            CheckMaxAndInitZoom();
        }
        private void ZoomIn() {
            if (_refresh) return;
            _zoomFactor *= 2;
            CheckMaxAndInitZoom();
        }
        private void RectZoom() {
            _isRectZoom = true;
            _isCenter = false;
            MsgBox.Text = "Press left mouse button down and move in any direction to select the rectangle area, then release.";
        }
        private void Center() {
            _isRectZoom = false;
            _isCenter = true;
            MsgBox.Text = "Click on left mouse button to center on cursor position.";
        }
        private void CheckMaxAndInitZoom() {
            CheckMaxZoom();
            _details = FilterValue + _zoomFactor * 2;
            _refresh = true;
        }
        private void CheckMaxZoom() {
            if (_zoomFactor > _maxZoomFactor) {
                _zoomFactor = _maxZoomFactor;
                MsgBox.Text = "The max Zoom Factor is reached. If you need a deeper zoom, set Julia input to decimal and buy a Supercomputer.";
            } else {
                MsgBox.Text = _defaultMsg;
            }
        }
        private void Center(double x, double y) {      
            int width = Bitmap.PixelWidth,
                height = Bitmap.PixelHeight,
                px = (int)(x * width),
                py = (int)(y * height);                
            double middle = _resX / 2;
            if (px < middle) {
                _addX-= (double)((middle - px) / (double)(_zoomFactor * 500));
            } else {
                _addX+= (double)((px - middle) / (double)(_zoomFactor * 500));
            }
            middle = _resY / 2;
            if (py < middle) {
                _addY-= (double)((middle - py) / (double)(_zoomFactor * 500));
            } else {
                _addY+= (double)((py - middle) / (double)(_zoomFactor * 500));
            }
        }
        private void RePaintMandelbrot() { 
            _calculatedValues = new List<_pixelValue>();
            CalcMandelbrot(_resX, _resY, _details, _details);
            RepaintBitmap();
        }
        private void Paint() {
            foreach (_pixelValue p in _calculatedValues) {
                int i = (int)(p.Iterations * 100);
                if (i > _colors.Length - 1) {
                    i = _colors.Length - 1;
                } 
                PutPixel(p.X, p.Y, _colors[i]);
            }
        }
    }
}