﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PuyoTools.Core.Textures.Pvr
{
    public class PvrTextureEncoder
    {
        #region Fields
        private PvrPixelCodec pixelCodec; // Pixel codec
        private PvrDataCodec dataCodec;   // Data codec
        private PvrCompressionCodec compressionCodec; // Compression Codec

        private int paletteEntries; // Number of palette entries in the palette data

        private static readonly byte[] gbixMagicCode = { (byte)'G', (byte)'B', (byte)'I', (byte)'X' };
        private static readonly byte[] pvrtMagicCode = { (byte)'P', (byte)'V', (byte)'R', (byte)'T' };

        private byte[] encodedPaletteData;
        private byte[] encodedTextureData;
        private byte[][] encodedMipmapData;

        private Image<Bgra32> sourceImage;
        #endregion

        #region Texture Properties
        /// <summary>
        /// Gets the width.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the pixel format.
        /// </summary>
        public PvrPixelFormat PixelFormat { get; private set; }

        /// <summary>
        /// Gets the data format.
        /// </summary>
        public PvrDataFormat DataFormat { get; private set; }

        /// <summary>
        /// Gets or sets the compression format.
        /// </summary>
        public PvrCompressionFormat CompressionFormat
        {
            get => compressionFormat;
            set
            {
                if (!Enum.IsDefined(typeof(PvrCompressionFormat), value))
                {
                    throw new CannotDecodeTextureException($"Unknown compression format {value}.");
                }

                compressionFormat = value;
                compressionCodec = PvrCompressionCodec.GetCompressionCodec(compressionFormat);
            }
        }
        private PvrCompressionFormat compressionFormat = PvrCompressionFormat.None;

        /// <summary>
        /// Gets or sets the global index. If <see langword="null"/>, the GBIX header will not be written.
        /// </summary>
        public uint? GlobalIndex { get; set; }
        #endregion

        #region Constructors & Initalizers
        /// <summary>
        /// Opens a texture to encode from a file.
        /// </summary>
        /// <param name="file">Filename of the file that contains the texture data.</param>
        /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
        /// <param name="dataFormat">Data format to encode the texture to.</param>
        public PvrTextureEncoder(string file, PvrPixelFormat pixelFormat, PvrDataFormat dataFormat)
        {
            using (var stream = File.OpenRead(file))
            {
                Initialize(stream, pixelFormat, dataFormat);
            }
        }

        /// <summary>
        /// Opens a texture to encode from a stream.
        /// </summary>
        /// <param name="source">Stream that contains the texture data.</param>
        /// <param name="pixelFormat">Pixel format to encode the texture to.</param>
        /// <param name="dataFormat">Data format to encode the texture to.</param>
        public PvrTextureEncoder(Stream source, PvrPixelFormat pixelFormat, PvrDataFormat dataFormat)
        {
            Initialize(source, pixelFormat, dataFormat);
        }

        private void Initialize(Stream source, PvrPixelFormat pixelFormat, PvrDataFormat dataFormat)
        {
            // Set the pixel and data formats, and verify that we can encode to them.
            // Unlike with the decoder, an exception will be thrown here if a codec cannot be used to encode them.
            PixelFormat = pixelFormat;
            DataFormat = dataFormat;

            pixelCodec = PvrPixelCodec.GetPixelCodec(pixelFormat);
            if (pixelCodec is null)
            {
                throw new CannotDecodeTextureException($"Pixel format {PixelFormat:X2} is invalid or not supported for encoding.");
            }
            
            dataCodec = PvrDataCodec.GetDataCodec(dataFormat);
            if (dataCodec is null)
            {
                throw new CannotDecodeTextureException($"Data format {DataFormat:X2} is invalid or not supported for encoding.");
            }
            dataCodec.PixelCodec = pixelCodec;

            // Get the number of palette entries.
            // In a Small VQ encoded texture, it's determined by the texture dimensions.
            paletteEntries = dataCodec.PaletteEntries;
            if (DataFormat == PvrDataFormat.SmallVq)
            {
                if (Width <= 16)
                {
                    paletteEntries = 64; // Actually 16
                }
                else if (Width <= 32)
                {
                    paletteEntries = 128; // Actually 32
                }
                else if (Width <= 64)
                {
                    paletteEntries = 512; // Actually 128
                }
                else
                {
                    paletteEntries = 1024; // Actually 256
                }
            }
            else if (DataFormat == PvrDataFormat.SmallVqMipmaps)
            {
                if (Width <= 16)
                {
                    paletteEntries = 64; // Actually 16
                }
                else if (Width <= 32)
                {
                    paletteEntries = 256; // Actually 64
                }
                else
                {
                    paletteEntries = 1024; // Actually 256
                }
            }

            // Read the image.
            sourceImage = Image.Load<Bgra32>(source);

            Width = sourceImage.Width;
            Height = sourceImage.Height;
        }
        #endregion

        #region Encode Texture
        /// <summary>
        /// Encodes the texture. Also encodes the palette and mipmaps if needed.
        /// </summary>
        /// <returns>The byte array containing the encoded texture data.</returns>
        private byte[] EncodeTexture()
        {
            byte[] pixelData;

            // Encode as a palettized image.
            if (paletteEntries != 0)
            {
                // Create the quantizer and quantize the texture.
                IQuantizer<Bgra32> quantizer;
                IndexedImageFrame<Bgra32> imageFrame;
                var quantizerOptions = new QuantizerOptions
                {
                    MaxColors = paletteEntries,
                };

                if (TryBuildExactPalette(sourceImage, paletteEntries, out var palette))
                {
                    quantizer = new PaletteQuantizer(palette.Cast<Color>().ToArray(), quantizerOptions)
                        .CreatePixelSpecificQuantizer<Bgra32>(Configuration.Default);

                    imageFrame = quantizer.QuantizeFrame(sourceImage.Frames.RootFrame, new Rectangle(0, 0, sourceImage.Width, sourceImage.Height));
                }
                else
                {
                    quantizer = new WuQuantizer(quantizerOptions)
                        .CreatePixelSpecificQuantizer<Bgra32>(Configuration.Default);

                    imageFrame = quantizer.BuildPaletteAndQuantizeFrame(sourceImage.Frames.RootFrame, new Rectangle(0, 0, sourceImage.Width, sourceImage.Height));
                }

                // Encode mipmaps using the same quantizer
                if (dataCodec.HasMipmaps)
                {
                    encodedMipmapData = new byte[(int)Math.Log(Width, 2)][];

                    for (int i = 0, size = 1; i < encodedMipmapData.Length && size <= Width; i++, size <<= 1)
                    {
                        encodedMipmapData[i] = EncodeIndexedTexture(Resize(sourceImage, size, size), quantizer);
                    }
                }

                // Save the palette
                if (dataCodec.NeedsExternalPalette)
                {
                    Palette = new PvrPaletteEncoder(this, EncodePalette(imageFrame.Palette), imageFrame.Palette.Length);
                }
                else
                {
                    encodedPaletteData = EncodePalette(imageFrame.Palette);
                }

                pixelData = GetPixelDataAsBytes(imageFrame);
            }

            // Encode as an RGBA image.
            else
            {
                // Encode mipmaps
                if (dataCodec.HasMipmaps)
                {
                    encodedMipmapData = new byte[(int)Math.Log(Width, 2)][];

                    for (int i = 0, size = 1; i < encodedMipmapData.Length && size <= Width; i++, size <<= 1)
                    {
                        encodedMipmapData[i] = EncodeRgbaTexture(Resize(sourceImage, size, size));
                    }
                }

                // Encode & return the texture
                return EncodeRgbaTexture(sourceImage);
            }

            return dataCodec.Encode(pixelData, 0, Width, Height);
        }

        private byte[] EncodeRgbaTexture<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var pixelData = GetPixelDataAsBytes(image.Frames.RootFrame);
            return dataCodec.Encode(pixelData, 0, image.Width, image.Height);
        }

        private byte[] EncodeIndexedTexture<TPixel>(Image<TPixel> image, IQuantizer<TPixel> quantizer)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var imageFrame = quantizer.QuantizeFrame(image.Frames.RootFrame, new Rectangle(0, 0, image.Width, image.Height));
            var pixelData = GetPixelDataAsBytes(imageFrame);
            return dataCodec.Encode(pixelData, 0, image.Width, image.Height);
        }

        /// <summary>
        /// Encodes the palette.
        /// </summary>
        /// <returns></returns>
        private byte[] EncodePalette(ReadOnlyMemory<Bgra32> palette)
        {
            var bytesPerPixel = pixelCodec.Bpp / 8;
            var paletteData = MemoryMarshal.AsBytes(palette.Span).ToArray();
            var encodedPaletteData = new byte[palette.Length * bytesPerPixel];

            for (var i = 0; i < palette.Length; i++)
            {
                pixelCodec.EncodePixel(paletteData, 4 * i, encodedPaletteData, i * bytesPerPixel);
            }

            return encodedPaletteData;
        }

        /// <summary>
        /// Gets if an external palette file will be created after encoding.
        /// </summary>
        public bool NeedsExternalPalette => dataCodec.NeedsExternalPalette;

        /// <summary>
        /// Gets the palette that was created after encoding.
        /// </summary>
        /// <remarks>
        /// This property will be <see langword="null"/> until <see cref="Save(string)"/> or <see cref="Save(Stream)"/> is invoked
        /// and <see cref="NeedsExternalPalette"/> is <see langword="true"/>.
        /// </remarks>
        public PvrPaletteEncoder Palette { get; private set; }

        /// <summary>
        /// Saves the encoded texture to the specified file.
        /// </summary>
        /// <param name="file">Name of the file to save the data to.</param>
        public void Save(string file)
        {
            using (var stream = File.OpenWrite(file))
            {
                Save(stream);
            }
        }

        /// <summary>
        /// Saves the encoded texture to the specified stream.
        /// </summary>
        /// <param name="destination">The stream to save the texture to.</param>
        public void Save(Stream destination)
        {
            var writer = new BinaryWriter(destination);

            if (encodedTextureData is null)
            {
                encodedTextureData = EncodeTexture();
            }

            // Get the expected length of the texture data including palette and mipmaps.
            var expectedLength = encodedTextureData.Length;
            if (encodedPaletteData != null)
            {
                expectedLength += encodedPaletteData.Length;
            }
            if (encodedMipmapData != null)
            {
                expectedLength += encodedMipmapData.Sum(x => x.Length);

                if (DataFormat == PvrDataFormat.SquareTwiddledMipmaps)
                {
                    expectedLength += pixelCodec.Bpp / 8;
                }
                else if (DataFormat == PvrDataFormat.SquareTwiddledMipmapsAlt)
                {
                    expectedLength += 3 * pixelCodec.Bpp / 8;
                }
            }

            // If RLE compression is used, then reserve bytes at the beginning for the uncompressed length.
            if (CompressionFormat == PvrCompressionFormat.Rle)
            {
                if (GlobalIndex != null)
                {
                    writer.WriteInt32(expectedLength + 32); // Length of the GBIX chunk + PVRT chunk
                }
                else
                {
                    writer.WriteInt32(expectedLength + 16); // Length of the PVRT chunk
                }
            }

            // Write out the GBIX header if a global index is present.
            if (GlobalIndex != null)
            {
                writer.Write(gbixMagicCode);
                writer.WriteInt32(8); // Length of the GBIX chunk minus 8. Always 8.
                writer.WriteUInt32(GlobalIndex.Value);
                writer.WriteInt32(0); // Always 0.
            }

            // Write out the PVRT header
            writer.Write(pvrtMagicCode);
            writer.WriteInt32(expectedLength + 8); // Length of the PVRT chunk minus 8.
            writer.WriteByte((byte)PixelFormat);
            writer.WriteByte((byte)DataFormat);
            writer.WriteInt16(0); // Always 0.
            writer.WriteUInt16((ushort)Width);
            writer.WriteUInt16((ushort)Height);

            // Write out the palette if an internal palette is present.
            if (encodedPaletteData != null)
            {
                writer.Write(encodedPaletteData);
            }

            // Write out the mipmaps if present.
            if (encodedMipmapData != null)
            {
                if (DataFormat == PvrDataFormat.SquareTwiddledMipmaps)
                {
                    writer.Write(new byte[pixelCodec.Bpp / 8]);
                }
                else if (DataFormat == PvrDataFormat.SquareTwiddledMipmapsAlt)
                {
                    writer.Write(new byte[3 * pixelCodec.Bpp / 8]);
                }

                foreach (var encodedMipmapDataItem in encodedMipmapData)
                {
                    writer.Write(encodedMipmapDataItem);
                }
            }

            // Write out the texture data.
            if (compressionCodec != null)
            {
                compressionCodec.Compress(new MemoryStream(encodedTextureData), destination, pixelCodec, dataCodec);
            }
            else
            {
                writer.Write(encodedTextureData);
            }
        }

        private Image<TPixel> Resize<TPixel>(Image<TPixel> image, int width, int height)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var newImage = image.Clone();
            newImage.Mutate(x => x.Resize(width, height));

            return newImage;
        }

        private static bool TryBuildExactPalette<TPixel>(Image<TPixel> image, int maxColors, out IList<TPixel> palette)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            palette = null;
            var newPalette = new List<TPixel>(maxColors);

            for (var y = 0; y < image.Height; y++)
            {
                var row = image.GetPixelRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    if (!newPalette.Contains(row[x]))
                    {
                        // If there are too many colors, then an exact palette cannot be built.
                        if (newPalette.Count == maxColors)
                        {
                            return false;
                        }

                        newPalette.Add(row[x]);
                    }
                }
            }

            palette = newPalette;

            return true;
        }

        private static byte[] GetPixelDataAsBytes<TPixel>(ImageFrame<TPixel> imageFrame)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (!imageFrame.TryGetSinglePixelSpan(out var pixelSpan))
            {
                return MemoryMarshal.AsBytes(pixelSpan).ToArray();
            }
            
            var data = new TPixel[imageFrame.Width * imageFrame.Height];

            for (var y = 0; y < imageFrame.Height; y++)
            {
                var row = imageFrame.GetPixelRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    data[(y * imageFrame.Width) + x] = row[x];
                }
            }

            return MemoryMarshal.AsBytes<TPixel>(data).ToArray();
        }

        private static byte[] GetPixelDataAsBytes<TPixel>(IndexedImageFrame<TPixel> imageFrame)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var data = new byte[imageFrame.Width * imageFrame.Height];

            for (var y = 0; y < imageFrame.Height; y++)
            {
                var row = imageFrame.GetPixelRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    data[(y * imageFrame.Width) + x] = row[x];
                }
            }

            return data;
        }
        #endregion
    }
}