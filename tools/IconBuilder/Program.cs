using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length != 2) throw new ArgumentException("Usage: IconBuilder input.png output.ico");
var sizes = new[] { 256, 128, 64, 48, 32, 24, 16 };
using var source = new Bitmap(args[0]);
var images = new List<byte[]>();
foreach (var size in sizes)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, size, size);
    }
    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    images.Add(stream.ToArray());
}

using var output = new BinaryWriter(File.Create(args[1]));
output.Write((ushort)0);
output.Write((ushort)1);
output.Write((ushort)images.Count);
var offset = 6 + images.Count * 16;
for (var index = 0; index < images.Count; index++)
{
    var size = sizes[index];
    output.Write((byte)(size == 256 ? 0 : size));
    output.Write((byte)(size == 256 ? 0 : size));
    output.Write((byte)0);
    output.Write((byte)0);
    output.Write((ushort)1);
    output.Write((ushort)32);
    output.Write(images[index].Length);
    output.Write(offset);
    offset += images[index].Length;
}
foreach (var image in images) output.Write(image);
