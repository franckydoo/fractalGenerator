using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace AvaloniaFractalGenerator
{
    public class MandelBrotModel
    {
        
        private readonly Action _invalidate;

        private class PixelValue {
            public double X {get;set;}
            public double Y {get;set;}
            public double Iterations {get;set;} 
        }
        public int[] pixels {get; set;}
        private List<PixelValue> calculatedValues {get; set;}
        public int MaxAngle {get;set;}
        public double FreqRed {get;set;}
        public double PhaseRed {get;set;}
        public double FreqGreen {get;set;}
        public double PhaseGreen {get;set;}
        public double FreqBlue {get;set;}
        public double PhaseBlue {get;set;}
        private Color[] colors {get; set;}
        private int _delayMs = 1;
        public int BlurValue {get; set;}

        public MandelBrotModel(Action invalidate)
        {
            _invalidate = invalidate;

            ResetCommand = new DelegateCommand(Reset);
            ZoomInCommand = new DelegateCommand(ZoomIn);
            ZoomOutCommand = new DelegateCommand(ZoomOut);
            RectangleZoomCommand = new DelegateCommand(Reset);


            // Bgra8888 is device-native and much faster.
            Bitmap = new WritableBitmap(1920, 1080, PixelFormat.Bgra8888);        
            BlurValue = 3;                          
            pixels = new int[1920*1080];

            MaxAngle = 1000000;

            FreqRed = 0.00015;
            PhaseRed = 0;
            FreqGreen = 0.00015;
            PhaseGreen = 1;
            FreqBlue = 0.00015;
            PhaseBlue = 0;
            ZoomFactor = 64;
            calculatedValues = new List<PixelValue>(); 
            CalcMandelbrot(1920, 1080, 50, 50);
            Reset();

            Task.Run(() => TaskRun());
        }

        public WritableBitmap Bitmap { get; }
        public double OffsetX {get; set;}
        public double OffsetY {get; set;}

        public double PosX {get; set;}
        public double PosY {get; set;}

        public int DelayMsInverted
        {
            get => MaxDelay - _delayMs;
            set => _delayMs = MaxDelay - value;
        }

        public int MaxDelay => 16;

        public ICommand ResetCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand CenterCommand { get; }
        public ICommand RectangleZoomCommand { get; }

        public IEnumerable<Color> Brushes => new[]
        {
            Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Cyan, Colors.Blue,
            Color.FromArgb(250, 0, 0, 0)
        };

        public Color SelectedBrush { get; set; } = Colors.Red;

        public void PutPixel(double x, double y, Color? color = null, int size = 1)
        {
            // Convert relative to absolute.
            var width = Bitmap.PixelWidth;
            var height = Bitmap.PixelHeight;

            var px = (int) (x * width);
            var py = (int) (y * height);
            
            var c = color ?? SelectedBrush;
        
            var pixel = c.B + ((uint) c.G << 8) + ((uint) c.R << 16) + ((uint) c.A << 24);
    
            for (var x0 = px - size; x0 <= px + size; x0++)
            for (var y0 = py - size; y0 <= py + size; y0++)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    int pos = width * y0 + x0;
                    pixels[pos] = (int)pixel;
                }
            }
        }

        public unsafe void PutPixelPtr(int pos, int pixel)
        {   
            using (var buf = Bitmap.Lock())
            {        
                var ptr = (uint*) buf.Address;
                ptr += (uint)pos;
                pixels[pos] = (int)pixel;
                *ptr = (uint)pixel;
            }
        }

        private void Reset() 
        {
            colors = GenerateColors(100000);
            ResetBitmap();
            Paint();
            BoxBlur(Bitmap, BlurValue);
        }

        private void BoxBlur(WritableBitmap bmp, int range)
        {        
            BoxBlurHorizontal(bmp, range);
            BoxBlurVertical(bmp, range);
        }
        
        private void BoxBlurHorizontal(WritableBitmap bmp, int range)
        {
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            int halfRange = range / 2;
            int index = 0;
            int[] newColors = new int[w];
        
            for (int y = 0; y < h; y++)
            {
                int hits = 0;
                int r = 0;
                int g = 0;
                int b = 0;
                for (int x = -halfRange; x < w; x++)
                {
                    int oldPixel = x - halfRange - 1;
                    if (oldPixel >= 0)
                    {
                        int col = pixels[index + oldPixel];
                        if (col != 0)
                        {
                            r -= ((byte)(col >> 16));
                            g -= ((byte)(col >> 8 ));
                            b -= ((byte)col);
                        }
                        hits--;
                    }
        
                    int newPixel = x + halfRange;
                    if (newPixel < w)
                    {
                        int col = pixels[index + newPixel];
                        if (col != 0)
                        {
                            r += ((byte)(col >> 16));
                            g += ((byte)(col >> 8 ));
                            b += ((byte)col);
                        }
                        hits++;
                    }
        
                    if (x >= 0)
                    {
                        int color =
                            (255 << 24)
                            | ((byte)(r / hits) << 16)
                            | ((byte)(g / hits) << 8 )
                            | ((byte)(b / hits));
        
                        newColors[x] = color;
                    }
                }
        
                for (int x = 0; x < w; x++)
                {
                    PutPixelPtr(index + x, newColors[x]);
                }
        
                index += w;
            }
        }
        
        private void BoxBlurVertical(WritableBitmap bmp, int range)
        {
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            int halfRange = range / 2;
        
            int[] newColors = new int[h];
            int oldPixelOffset = -(halfRange + 1) * w;
            int newPixelOffset = (halfRange) * w;
        
            for (int x = 0; x < w; x++)
            {
                int hits = 0;
                int r = 0;
                int g = 0;
                int b = 0;
                int index = -halfRange * w + x;
                for (int y = -halfRange; y < h; y++)
                {
                    int oldPixel = y - halfRange - 1;
                    if (oldPixel >= 0)
                    {
                        int col = pixels[index + oldPixelOffset];
                        if (col != 0)
                        {
                            r -= ((byte)(col >> 16));
                            g -= ((byte)(col >> 8 ));
                            b -= ((byte)col);
                        }
                        hits--;
                    }
        
                    int newPixel = y + halfRange;
                    if (newPixel < h)
                    {
                        int col = pixels[index + newPixelOffset];
                        if (col != 0)
                        {
                            r += ((byte)(col >> 16));
                            g += ((byte)(col >> 8 ));
                            b += ((byte)col);
                        }
                        hits++;
                    }
        
                    if (y >= 0)
                    {
                        int color =
                            (255 << 24)
                            | ((byte)(r / hits) << 16)
                            | ((byte)(g / hits) << 8 )
                            | ((byte)(b / hits));
        
                        newColors[y] = color;
                    }
        
                    index += w;
                }
        
                for (int y = 0; y < h; y++)
                {
                    PutPixelPtr(y * w + x, newColors[y]);
                }
            }
        }

        public Color[] GenerateColors(int number) {
            var colors = new List<Color>(number);
            double step = MaxAngle / number;
            for(int i = 0; i < number; ++i) {
                var r = (Math.Sin(FreqRed * i * step + PhaseRed) + 1) * .5;
                var g = (Math.Sin(FreqGreen * i * step + PhaseGreen) + 1) * .5;
                var b = (Math.Sin(FreqBlue * i * step + PhaseBlue) + 1) * .5;
                colors.Add(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            }
            return colors.ToArray();
        }

        private unsafe void ResetBitmap()
        {  
            pixels = new int[1920*1080];
            using (var buf = Bitmap.Lock())
            {
                var ptr = (uint*)buf.Address;

                var w = Bitmap.PixelWidth;
                var h = Bitmap.PixelHeight;

                // Clear.
                for (var i = 0; i < w * h; i++)
                {
                    *(ptr + i) = 0;
                }
            }
        }

        private void TaskRun() {
            while (true)
            {
                _invalidate();
                Thread.Sleep(_delayMs);
            }
        }

        private double Julia (double x, double y, double xAdd, double yAdd, int maxResultQuad, int max) {
            int remain = max;
            double xx = x * x, 
                   yy = y * y, 
                   xy = x * y, 
                   resultQuad = xx + yy;

            while(resultQuad <= maxResultQuad && remain-- > 0) {
                x  = xx - yy + xAdd;
                y  = xy + xy + yAdd;
                xx = x * x;
                yy = y * y;
                xy = x * y;
                resultQuad = xx + yy;
            }
            return  max - remain - Math.Log(Math.Log(resultQuad) / Math.Log(4)) / Math.Log(2);
        }

        private void CalcMandelbrot(int width, int height, int maxResultQuad, int max)
        {
            for (int row = 0; row < height; row++) {
                for (int col = 0; col < width; col++) {
                    double x = (col - width / 2.0) * 4.0 / width,
                           y = (row - height / 2.0) * 4.0 / width, 
                           i = Julia(x / ZoomFactor - 1.2395, y / ZoomFactor + 0.1, x / ZoomFactor - 1.2395, y / ZoomFactor + 0.1, maxResultQuad, max);
                    if (i < max && i >= 0) 
                        calculatedValues.Add(new PixelValue {
                            X = (double)col / width, 
                            Y = (double)row / height, 
                            Iterations = i
                        });                    
                }
            }
        }

        private int ZoomFactor {get; set;}

        private void ZoomOut() {
            calculatedValues = new List<PixelValue>();
            ZoomFactor /= 2;
            if (ZoomFactor < 1) {
                ZoomFactor = 1;
            }
            CalcMandelbrot(1920, 1080, 50, 50);
            Reset();
        }

        private void ZoomIn() {
            calculatedValues = new List<PixelValue>();
            ZoomFactor *= 2;
            CalcMandelbrot(1920, 1080, 50, 50);
            Reset();
        }

        public void CenterBitmap(double x, double y) {
            var width = Bitmap.PixelWidth;
            var height = Bitmap.PixelHeight;

            var px = (int) (x * width);
            var py = (int) (y * height);
        }

        public unsafe void StoreFile(string fileName)
        {
            MemoryStream data = new MemoryStream();
            Bitmap.Save(data); 
            data.Seek(0, SeekOrigin.Begin);
            try{
                using (var img = Image.Load(data, new PngDecoder()))
                {
                    img.Save(fileName);
                }
            } catch(Exception e){
                Console.Write(e.Message);
            }
        }

        private void Paint() {
            foreach (var p in calculatedValues) {
                PutPixel(p.X, p.Y, colors[(int)(p.Iterations * 100)]);
            }
        }
    }
}
