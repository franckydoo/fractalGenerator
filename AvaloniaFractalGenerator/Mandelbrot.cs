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
        private double OldDetails {get; set;}
        private List<PixelValue> calculatedValues {get; set;}
        private bool IsRectZoom = false;
        private bool IsCenter = false;
        private const int ResX = 1920;
        private const int ResY = 1080;
        private const int MaxZoomFactor = 4194304;
        private double AddX {get;set;}
        private double AddY {get;set;}
        private Color[] colors {get; set;}
        private int ZoomFactor {get; set;}
        private int _delayMs = 1;
        public TextBox MsgBox {get;set;}
        public Rectangle Rect {get;set;}
        public int[] pixels {get; set;}
        public double Details {get; set;}
        public int FilterValue {get; set;}
        public int MaxAngle {get;set;}
        public double FreqRed {get;set;}
        public double PhaseRed {get;set;}
        public double FreqGreen {get;set;}
        public double PhaseGreen {get;set;}
        public double FreqBlue {get;set;}
        public double PhaseBlue {get;set;}
        public int BlurValue {get; set;}
        public WritableBitmap Bitmap { get; }
        public WritableBitmap Overlay { get; }
        public double OffsetX {get; set;}
        public double OffsetY {get; set;}
        public ICommand ResetCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand CenterCommand { get; }
        public ICommand RectZoomCommand { get; }

        public MandelBrotModel(Action invalidate)
        {
            _invalidate = invalidate;

            ResetCommand = new DelegateCommand(Reset);
            ZoomInCommand = new DelegateCommand(ZoomIn);
            ZoomOutCommand = new DelegateCommand(ZoomOut);
            CenterCommand = new DelegateCommand(Center);
            RectZoomCommand = new DelegateCommand(RectZoom);

            Bitmap = new WritableBitmap(ResX, ResY, PixelFormat.Bgra8888);   
            Overlay = new WritableBitmap(ResX, ResY, PixelFormat.Bgra8888);   
  
            BlurValue = 3;  
            AddX = -1.2395;
            AddY = 0.1;       
            pixels = new int[ResX*ResY];
            MaxAngle = 1000000;
            FreqRed = 0.0015;
            PhaseRed = 0;
            FreqGreen = 0.0015;
            PhaseGreen = 1;
            FreqBlue = 0.0015;
            PhaseBlue = 0;
            ZoomFactor = 64;
            FilterValue = 50;
            Details = FilterValue + ZoomFactor * 2;

            RePaintMandelbrot();

            Task.Run(() => TaskRun());
        }

        private void PutPixel(double x, double y, Color c, int size = 1)
        {
            int width = Bitmap.PixelWidth,
                height = Bitmap.PixelHeight,
                px = (int) (x * width),
                py = (int) (y * height),
                pixel = (int)(c.B + ((uint) c.G << 8) + ((uint) c.R << 16) + ((uint) c.A << 24));
    
            for (int x0 = px - size; x0 <= px + size; x0++) {
                for (int y0 = py - size; y0 <= py + size; y0++)
                {
                    if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                    {
                        int pos = width * y0 + x0;
                        pixels[pos] = pixel;
                    }
                }
            }
        }

        private unsafe void PutPixelPtr(int pos, int pixel)
        {   
            using (var buf = Bitmap.Lock())
            {        
                uint* ptr = (uint*) buf.Address;
                ptr += (uint)pos;
                pixels[pos] = (int)pixel;
                *ptr = (uint)pixel;
            }
        }

        private void Reset() 
        {
            colors = GenerateColors(1000000);
            ResetBitmap();
            Details = FilterValue + ZoomFactor * 2;
            if (OldDetails != Details) {
                calculatedValues = new List<PixelValue>(); 
                CalcMandelbrot(ResX, ResY, Details, Details);
            }
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
            int w = bmp.PixelWidth,
                h = bmp.PixelHeight,
                halfRange = range / 2,
                index = 0;
            int[] newColors = new int[w];
        
            for (int y = 0; y < h; y++)
            {
                int hits = 0,
                    r = 0,
                    g = 0,
                    b = 0;
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
            int w = bmp.PixelWidth,
                h = bmp.PixelHeight,
                halfRange = range / 2,
                oldPixelOffset = -(halfRange + 1) * w,
                newPixelOffset = (halfRange) * w;        
            int[] newColors = new int[h];
        
            for (int x = 0; x < w; x++)
            {
                int hits = 0,
                    r = 0,
                    g = 0,
                    b = 0,
                    index = -halfRange * w + x;
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

        private Color[] GenerateColors(int number) {
            List<Color> colors = new List<Color>(number);
            double step = MaxAngle / number;
            for(int i = 0; i < number; ++i) {
                double r = (Math.Sin(FreqRed * i * step + PhaseRed) + 1) * .5,
                       g = (Math.Sin(FreqGreen * i * step + PhaseGreen) + 1) * .5,
                       b = (Math.Sin(FreqBlue * i * step + PhaseBlue) + 1) * .5;
                colors.Add(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            }
            return colors.ToArray();
        }

        private unsafe void ResetBitmap()
        {  
            pixels = new int[ResX*ResY];
            using (var buf = Bitmap.Lock())
            {
                uint* ptr = (uint*)buf.Address;
                int w = Bitmap.PixelWidth,
                    h = Bitmap.PixelHeight;

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

        private void CalcMandelbrot(int width, int height, double maxResultQuad, double max)
        {
            OldDetails = max; 
            for (int row = 0; row < height; row++) {
                for (int col = 0; col < width; col++) {
                    double x = (double)((col - width / 2.0) * 4.0 / width),
                           y = (double)((row - height / 2.0) * 4.0 / width), 
                           i = Julia(x / ZoomFactor + AddX, 
                               y / ZoomFactor + AddY, 
                               x / ZoomFactor + AddX, 
                               y / ZoomFactor + AddY, maxResultQuad, max);
                    if (i < max && i >= 0) 
                        calculatedValues.Add(new PixelValue {
                            X = (double)col / width, 
                            Y = (double)row / height, 
                            Iterations = i
                        });                    
                }
            }
        }

        private void ZoomOut() {
            ZoomFactor /= 2;
            if (ZoomFactor < 1) {
                ZoomFactor = 1;
            }
            Details = FilterValue + ZoomFactor * 2;
            RePaintMandelbrot();
        }

        private void ZoomIn() {
            ZoomFactor *= 2;
            //TODO RectZoom
            if (ZoomFactor > MaxZoomFactor) {
                ZoomFactor = MaxZoomFactor;
                //TODO InfoBox
                MsgBox.Text = "The max Zoom Factor is reached. If you need a deeper zoom, set Julia input to decimal and buy a supercomputer.";
            }
            Details = FilterValue + ZoomFactor * 2;
            RePaintMandelbrot();
        }



        private void Center(double x, double y) {      
            int width = Bitmap.PixelWidth,
                height = Bitmap.PixelHeight,
                px = (int)(x * width),
                py = (int)(y * height);                
            double middle = ResX / 2;
            if (px < middle) {
                AddX-= (double)((middle - px) / (double)(ZoomFactor * 500));
            }
            else {
                AddX+= (double)((px - middle) / (double)(ZoomFactor * 500));
            }
            middle = ResY / 2;
            if (py < middle) {
                AddY-= (double)((middle - py) / (double)(ZoomFactor * 500));
            }
            else {
                AddY+= (double)((py - middle) / (double)(ZoomFactor * 500));
            }
        }

        public void CenterBitmap(double x, double y) {
            if (IsCenter) {
                Center(x,y);
                RePaintMandelbrot();
                IsCenter = false;
            }
        }

        private void RePaintMandelbrot() {
            calculatedValues = new List<PixelValue>();
            CalcMandelbrot(ResX, ResY, Details, Details);
            Reset();
        }

        private void RectZoom() {
            IsRectZoom = true;
            IsCenter = false;
        }

        private void Center() {
            IsRectZoom = false;
            IsCenter = true;
        }
        public void RectangleInit(double x, double y) {
            if (IsRectZoom) {
                (OffsetX, OffsetY) = (x, y);
            }
        }

        public void Rectangle(double x, double y) {
            if (IsRectZoom) {                
                Canvas.SetLeft(Rect, (x < OffsetX ? x : OffsetX));
                Canvas.SetTop(Rect, (y < OffsetY ? y : OffsetY));
                Rect.Width = (x > OffsetX ? x - OffsetX : OffsetX - x);
                Rect.Height = (y > OffsetY ? y - OffsetY : OffsetY - y);
            }
        }

        public void RectangleZoom(double wWidth, double wHeight) {
            if (IsRectZoom) {
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
                IsRectZoom = false;
                Center(left + (width / 2), top + (height / 2));
                ZoomFactor = (int)ZoomFactor * (100 / (xProz > yProz ? xProz : yProz)); 
                RePaintMandelbrot();
            }
        }

        public unsafe void StoreFile(string fileName)
        {
            MemoryStream data = new MemoryStream();
            Bitmap.Save(data);
            data.Seek(0, SeekOrigin.Begin);
            try{
                using (var img = SixLabors.ImageSharp.Image.Load(data, new PngDecoder()))
                {
                    img.Save(fileName);
                }
            } catch(Exception e){
                Console.Write(e.Message);
            }
        }

        private void Paint() {
            foreach (PixelValue p in calculatedValues) {
                int i = (int)(p.Iterations * 100);
                if (i > colors.Length - 1) {
                    i = colors.Length - 1;
                } 
                PutPixel(p.X, p.Y, colors[i]);
            }
        }
    }
}