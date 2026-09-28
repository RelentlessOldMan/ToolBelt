using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class ImageBufferTests
    {
        public void SetAndGetPixel()
        {
            var img = new ImageBuffer(4, 3);
            img.SetPixel(2, 1, Rgba.Red);
            Check.Equal(Rgba.Red, img.GetPixel(2, 1));
            Check.Equal(new Rgba(0, 0, 0, 0), img.GetPixel(0, 0)); // default transparent
        }

        public void FillSetsEveryPixel()
        {
            var img = new ImageBuffer(3, 3, Rgba.Blue);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Check.Equal(Rgba.Blue, img.GetPixel(x, y));
        }

        public void DrawLineHitsEndpoints()
        {
            var img = new ImageBuffer(5, 5);
            img.DrawLine(0, 0, 4, 4, Rgba.White);
            Check.Equal(Rgba.White, img.GetPixel(0, 0));
            Check.Equal(Rgba.White, img.GetPixel(4, 4));
            Check.Equal(Rgba.White, img.GetPixel(2, 2)); // diagonal midpoint
        }

        public void DrawLineClipsOutOfBounds()
        {
            var img = new ImageBuffer(5, 5);
            img.DrawLine(-3, 2, 10, 2, Rgba.Green); // extends past both edges
            Check.Equal(Rgba.Green, img.GetPixel(0, 2));
            Check.Equal(Rgba.Green, img.GetPixel(4, 2));
        }

        public void DrawRectangleOutlineAndFilled()
        {
            var outline = new ImageBuffer(5, 5);
            outline.DrawRectangle(1, 1, 3, 3, Rgba.Red);
            Check.Equal(Rgba.Red, outline.GetPixel(1, 1));       // corner
            Check.Equal(new Rgba(0, 0, 0, 0), outline.GetPixel(2, 2)); // hollow center

            var filled = new ImageBuffer(5, 5);
            filled.DrawRectangle(1, 1, 3, 3, Rgba.Red, filled: true);
            Check.Equal(Rgba.Red, filled.GetPixel(2, 2));         // center filled
        }

        public void Blit()
        {
            var dest = new ImageBuffer(6, 6);
            var src = new ImageBuffer(2, 2, Rgba.White);
            dest.Blit(src, 3, 3);
            Check.Equal(Rgba.White, dest.GetPixel(3, 3));
            Check.Equal(Rgba.White, dest.GetPixel(4, 4));
            Check.Equal(new Rgba(0, 0, 0, 0), dest.GetPixel(0, 0)); // untouched
        }

        public void OutOfBoundsPixel_Throws()
        {
            var img = new ImageBuffer(3, 3);
            Check.Throws<ArgumentOutOfRangeException>(() => img.SetPixel(3, 0, Rgba.Red));
            Check.Throws<ArgumentOutOfRangeException>(() => img.GetPixel(0, -1));
        }

        public void InvalidSize_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(0, 5));
            Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(5, -1));
        }
    }
}
