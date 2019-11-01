﻿using System.Collections.Generic;
using System.Linq;
using ESCPOS_NET.Utilities;
using System;
using System.Drawing;

namespace ESCPOS_NET.Emitters
{
    public abstract partial class BaseCommandEmitter : ICommandEmitter
    {
        private byte[] GetImageHeader(int commandLength)
        {
            byte[] lengths = new byte[4];
            int i = 0;
            while (commandLength > 0)
            {
                lengths[i] = (byte)(commandLength & 0xFF);
                commandLength >>= 8;
                i++;
            }
            if (i >= 3)
            {
                return new byte[] { Cmd.GS, Images.ImageCmd8, Images.ImageCmdL, lengths[0], lengths[1], lengths[2], lengths[3] };
            }
            else
            {
                return new byte[] { Cmd.GS, Images.ImageCmdParen, Images.ImageCmdL, lengths[0], lengths[1] };
            }
        }


        /* Image Commands */
        public byte[] SetImageDensity(bool isHiDPI)
        {
            ByteArrayBuilder builder = new ByteArrayBuilder();
            byte dpiSetting = isHiDPI ? (byte)0x33 : (byte)0x32; // TODO: is this right??
            byte[] baseCommand = new byte[] { 0x30, 0x31, dpiSetting, dpiSetting };
            builder.Append(GetImageHeader(baseCommand.Length));
            builder.Append(baseCommand);
            return builder.ToArray();
        }

        public byte[] BufferImage(byte[] image, int maxWidth = -1, int color = 1)
        {
            ByteArrayBuilder imageCommand = new ByteArrayBuilder();

            byte colorByte;
            switch (color)
            {
                case 2:
                    colorByte = 0x32;
                    break;
                case 3:
                    colorByte = 0x33;
                    break;
                default:
                    colorByte = 0x31;
                    break;
            }

            Bitmap img = null, bmp = null;
            try
            {
                img = new Bitmap(new System.IO.MemoryStream(image), false);
                int width = img.Width;
                int height = img.Height;
                // Use native width/height of image without resizing if maxWidth is default value.
                if (maxWidth != -1)
                {
                    width = maxWidth;

                    // Get closest height that's the same aspect ratio as the width.
                    height = (int)(maxWidth * (Convert.ToDouble(img.Height) / img.Width));

                    float scale = Math.Min((float)width / (float)img.Width, (float)height / (float)img.Height);
                    bmp = new Bitmap((int)width, (int)height);
                    var graph = Graphics.FromImage(bmp);

                    var scaleWidth = (int)(img.Width * scale);
                    var scaleHeight = (int)(img.Height * scale);

                    var brush = new SolidBrush(Color.White);
                    graph.FillRectangle(brush, new RectangleF(0, 0, width, height));
                    graph.DrawImage(img, ((int)width - scaleWidth) / 2, ((int)height - scaleHeight) / 2, scaleWidth, scaleHeight);
                    img.Dispose();
                    img = bmp;
                    //img.Mutate(x => x.Resize(width, height));
                }
                byte widthL = (byte)(width);
                byte widthH = (byte)(width >> 8);
                byte heightL = (byte)(height);
                byte heightH = (byte)(height >> 8);

                imageCommand.Append(new byte[] { 0x30, 0x70, 0x30, 0x01, 0x01, colorByte, widthL, widthH, heightL, heightH });
                // TODO: test making a List<byte> if it's faster than using ByteArrayBuilder.

                // Bit pack every 8 horizontal bits into a single byte.
                for (int y = 0; y < img.Height; y++)
                {
                    //Span<Rgba32> pixelRowSpan = img.GetPixelRowSpan(y);
                    byte buffer = 0x00;
                    int bufferCount = 7;
                    for (int x = 0; x < img.Width; x++)
                    {
                        // Determine if pixel should be colored in.
                        //if ((0.30 * pixelRowSpan[x].R) + (0.59 * pixelRowSpan[x].G) + (0.11 * pixelRowSpan[x].B) <= 127)
                        var pixel = img.GetPixel(x, y);

                        if ((0.30 * pixel.R) + (0.59 * pixel.G) + (0.11 * pixel.B) <= 127)
                        {
                            buffer |= (byte)(0x01 << bufferCount);
                        }
                        if (bufferCount == 0)
                        {
                            bufferCount = 8;
                            imageCommand.Append(new byte[] { buffer });
                            buffer = 0x00;
                        }
                        bufferCount -= 1;
                    }
                    // For images not divisible by 8
                    if (bufferCount != 7)
                    {
                        imageCommand.Append(new byte[] { buffer });
                    }
                }
            }
            finally
            {
                if (img != null)
                    img.Dispose();

                if (bmp != null)
                    bmp.Dispose();
            }




            // Load image to print buffer
            byte[] imageCommandBytes = imageCommand.ToArray();
            ByteArrayBuilder response = new ByteArrayBuilder();
            response.Append(GetImageHeader(imageCommandBytes.Length));
            response.Append(imageCommandBytes);
            return response.ToArray();
        }

        public byte[] WriteImageFromBuffer()
        {
            // Print image that's already buffered.
            ByteArrayBuilder response = new ByteArrayBuilder();
            byte[] printCommandBytes = new byte[] { 0x30, 0x32 };
            response.Append(GetImageHeader(printCommandBytes.Length));
            response.Append(printCommandBytes);
            return response.ToArray();
        }
        public byte[] PrintImage(byte[] image, bool isHiDPI, int maxWidth = -1, int color = 1)
        {
            return ByteSplicer.Combine(SetImageDensity(isHiDPI), BufferImage(image, maxWidth, color), WriteImageFromBuffer());

        }
    }
}