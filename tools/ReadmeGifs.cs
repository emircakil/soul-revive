using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SoulsRevive
{
    static class ReadmeGifs
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory(args[0]);
            Write(Path.Combine(args[0],"day-night-cycle.gif"),176,delegate(int frame) {
                double progress=Math.Min(1,frame/159.0);
                bool sharpening=frame>=8&&frame<120;
                return PixelArt.Scene(1-progress,false,frame,5.5+15*progress,false,
                    sharpening?WorkAction.Sharpen:WorkAction.Idle,sharpening?frame-8:0);
            });
            Write(Path.Combine(args[0],"solaire-chill.gif"),PixelArt.TavernArrivalFrames+128,
                delegate(int frame) {return PixelArt.Tavern(frame);});
        }
        static void Write(string path,int count,Func<int,Bitmap> render)
        {
            using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
                writer.Write((ushort)516);writer.Write((ushort)276);
                writer.Write(new byte[]{0x70,0,0});
                writer.Write(new byte[]{0x21,0xFF,0x0B});
                writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                writer.Write(new byte[]{3,1,0,0,0});
                for(int frame=0;frame<count;frame++)
                using(var source=render(frame))using(var indexed=Indexed(source))using(var memory=new MemoryStream())
                {
                    indexed.Save(memory,ImageFormat.Gif);
                    byte[] gif=memory.ToArray();int cursor=13;
                    int paletteBytes=(gif[10]&128)!=0?3*(1<<((gif[10]&7)+1)):0;
                    int paletteStart=13;int paletteBits=gif[10]&7;cursor+=paletteBytes;
                    while(gif[cursor]==0x21)
                    {
                        cursor+=2;
                        while(gif[cursor]!=0)cursor+=1+gif[cursor];
                        cursor++;
                    }
                    if(gif[cursor]!=0x2C)throw new InvalidDataException("Missing GIF image descriptor.");
                    byte[] descriptor=new byte[10];Array.Copy(gif,cursor,descriptor,0,10);cursor+=10;
                    if((descriptor[9]&128)!=0)
                    {paletteBits=descriptor[9]&7;paletteStart=cursor;paletteBytes=3*(1<<(paletteBits+1));cursor+=paletteBytes;}
                    // Full opaque frames, local exact-color palettes, 8 fps (12/13 centiseconds).
                    writer.Write(new byte[]{0x21,0xF9,4,4,(byte)(frame%2==0?12:13),0,0,0});
                    descriptor[9]=(byte)((descriptor[9]&0x40)|0x80|paletteBits);
                    writer.Write(descriptor);writer.Write(gif,paletteStart,paletteBytes);
                    int dataStart=cursor;cursor++;
                    while(gif[cursor]!=0)cursor+=1+gif[cursor];
                    cursor++;writer.Write(gif,dataStart,cursor-dataStart);
                }
                writer.Write((byte)0x3B);
            }
            // Decode the final animation independently and verify every frame and delay.
            using(var decoded=Image.FromFile(path))
            {
                int actual=decoded.GetFrameCount(FrameDimension.Time);
                if(actual!=count||decoded.Width!=516||decoded.Height!=276)throw new Exception("Invalid animation dimensions or frame count.");
                byte[] delays=decoded.GetPropertyItem(0x5100).Value;
                int duration=0;
                for(int frame=0;frame<count;frame++)
                {
                    decoded.SelectActiveFrame(FrameDimension.Time,frame);
                    duration+=BitConverter.ToInt32(delays,frame*4);
                    if(frame==count/2)decoded.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,Path.GetFileNameWithoutExtension(path)+".preview.png"),ImageFormat.Png);
                }
                Console.WriteLine(Path.GetFileName(path)+": "+count+" frames, "+duration/100.0+" s, "+new FileInfo(path).Length+" bytes");
            }
        }
        static Bitmap Indexed(Bitmap source)
        {
            var colors=new List<Color>();var lookup=new Dictionary<int,byte>();
            byte[] pixels=new byte[source.Width*source.Height];
            for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
            {
                Color color=source.GetPixel(x,y);byte index;
                if(!lookup.TryGetValue(color.ToArgb(),out index))
                {
                    if(colors.Count==256)throw new InvalidDataException("Sprite exceeds exact GIF palette.");
                    index=(byte)colors.Count;colors.Add(color);lookup.Add(color.ToArgb(),index);
                }
                pixels[y*source.Width+x]=index;
            }
            var image=new Bitmap(source.Width*3,source.Height*3,PixelFormat.Format8bppIndexed);
            var palette=image.Palette;for(int i=0;i<colors.Count;i++)palette.Entries[i]=colors[i];image.Palette=palette;
            var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.WriteOnly,PixelFormat.Format8bppIndexed);
            try
            {
                byte[] bytes=new byte[data.Stride*image.Height];
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)bytes[y*data.Stride+x]=pixels[(y/3)*source.Width+x/3];
                Marshal.Copy(bytes,0,data.Scan0,bytes.Length);
            }
            finally {image.UnlockBits(data);}
            return image;
        }
    }
}
