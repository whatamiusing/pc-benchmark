using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace WaiuBenchmark
{
    internal static class RenderStageArtwork
    {
        private static void Main(string[] args)
        {
            Directory.CreateDirectory(args[0]);
            var jpeg=ImageCodecInfo.GetImageEncoders().Single(codec=>codec.MimeType=="image/jpeg");
            for (int scene=0;scene<StageArtwork.Names.Length;scene++)
            {
                const int width=StageArtwork.FrameWidth, height=StageArtwork.FrameHeight;
                const int columns=StageArtwork.Columns, rows=StageArtwork.Rows;
                using (var sheet=new Bitmap(columns*width,rows*height,PixelFormat.Format24bppRgb))
                using (var canvas=Graphics.FromImage(sheet))
                using (var options=new EncoderParameters(1))
                {
                    GraphicsBenchmark.RenderArtwork(scene,columns*rows,width,height,(index,frame)=>
                        canvas.DrawImageUnscaled(frame,(index%columns)*width,(index/columns)*height));
                    options.Param[0]=new EncoderParameter(Encoder.Quality,91L);
                    sheet.Save(Path.Combine(args[0],StageArtwork.Names[scene]+".jpg"),jpeg,options);
                }
            }
        }
    }
}
