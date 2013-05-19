﻿using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VrSharp
{
    public abstract class VrTexture
    {
        #region Fields
        protected bool InitSuccess = false; // Initalization

        protected byte[] TextureData;  // Vr Texture Data
        protected byte[] RawImageData; // Raw Image Data

        protected VrPixelCodec PixelCodec; // Pixel Codec
        protected VrDataCodec DataCodec;   // Data Codec

        protected int ClutOffset; // Clut Offset
        protected int DataOffset; // Data Offset
        #endregion

        #region Texture Properties
        /// <summary>
        /// The texture's global index, or 0 if this texture does not have a global index defined.
        /// </summary>
        public uint GlobalIndex { get; protected set; }

        /// <summary>
        /// Width of the texture (in pixels).
        /// </summary>
        public ushort TextureWidth { get; protected set; }

        /// <summary>
        /// Height of the texture (in pixels).
        /// </summary>
        public ushort TextureHeight { get; protected set; }

        /// <summary>
        /// Offset of the GBIX (or GCIX) chunk in the texture file, or -1 if this chunk is not present.
        /// </summary>
        public int GbixOffset { get; protected set; }

        /// <summary>
        /// Offset of the PVRT (or GVRT) chunk in the texture file.
        /// </summary>
        public int PvrtOffset { get; protected set; }
        #endregion

        #region Constructors & Initalizers
        // Open a texture from a file.
        public VrTexture(string file)
        {
            try
            {
                TextureData = File.ReadAllBytes(file);
            }
            catch
            {
                TextureData = null;
            }

            if (TextureData != null)
            {
                InitSuccess = Initalize();
            }
            else
            {
                InitSuccess = false;
            }
        }

        // Open a texture from a byte array.
        public VrTexture(byte[] source) : this(source, 0, source.Length) { }

        public VrTexture(byte[] source, int offset, int length)
        {
            if (source == null || (offset == 0 && source.Length == length))
            {
                TextureData = source;
            }
            else if (source != null)
            {
                TextureData = new byte[length];
                Array.Copy(source, offset, TextureData, 0, length);
            }

            if (TextureData != null)
            {
                InitSuccess = Initalize();
            }
            else
            {
                InitSuccess = false;
            }
        }

        // Open a texture from a stream.
        public VrTexture(Stream source) : this(source, (int)(source.Length - source.Position)) { }

        public VrTexture(Stream source, int length)
        {
            try
            {
                TextureData = new byte[length];
                source.Read(TextureData, 0, length);
            }
            catch
            {
                TextureData = null;
            }

            if (TextureData != null)
            {
                InitSuccess = Initalize();
            }
            else
            {
                InitSuccess = false;
            }
        }

        protected abstract bool Initalize();
        #endregion

        #region Get Texture
        /// <summary>
        /// Get the texture as a byte array (clone of GetTextureAsArray).
        /// </summary>
        /// <returns></returns>
        public byte[] GetTexture()
        {
            return GetTextureAsArray();
        }

        /// <summary>
        /// Get the texture as a byte array.
        /// </summary>
        /// <returns></returns>
        public byte[] GetTextureAsArray()
        {
            if (!InitSuccess) return null;

            return ConvertRawToArray(DecodeTexture(), TextureWidth, TextureHeight);
        }

        /// <summary>
        /// Get the texture as a memory stream.
        /// </summary>
        /// <returns></returns>
        public MemoryStream GetTextureAsStream()
        {
            if (!InitSuccess) return null;

            return ConvertRawToStream(DecodeTexture(), TextureWidth, TextureHeight);
        }

        /// <summary>
        /// Get the texture as a System.Drawing.Bitmap object.
        /// </summary>
        /// <returns></returns>
        public Bitmap GetTextureAsBitmap()
        {
            if (!InitSuccess) return null;

            return ConvertRawToBitmap(DecodeTexture(), TextureWidth, TextureHeight);
        }

        /// <summary>
        /// Returns the decoded texture as an array containg raw 32-bit ARGB data.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            if (!InitSuccess) return null;

            return DecodeTexture();
        }

        /// <summary>
        /// Returns the decoded texture as a bitmap.
        /// </summary>
        /// <returns></returns>
        public Bitmap ToBitmap()
        {
            if (!InitSuccess) return null;

            byte[] data = DecodeTexture();

            Bitmap img = new Bitmap(TextureWidth, TextureHeight, PixelFormat.Format32bppArgb);
            BitmapData bitmapData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, img.PixelFormat);
            Marshal.Copy(data, 0, bitmapData.Scan0, data.Length);
            img.UnlockBits(bitmapData);

            return img;
        }

        /// <summary>
        /// Returns the decoded texture as a stream containg a PNG.
        /// </summary>
        /// <returns></returns>
        public MemoryStream ToStream()
        {
            if (!InitSuccess) return null;

            MemoryStream destination = new MemoryStream();
            ToBitmap().Save(destination, ImageFormat.Png);

            return destination;
        }

        /// <summary>
        /// Saves the decoded texture to the specified file.
        /// </summary>
        /// <param name="file">Name of the file to save the data to.</param>
        public void Save(string file)
        {
            if (!InitSuccess) return;

            ToBitmap().Save(file, ImageFormat.Png);
        }
        #endregion

        #region Get Texture Mipmap
        /// <summary>
        /// Get a mipmap in the texture as a byte array (clone of GetTextureMipmapAsArray).
        /// </summary>
        /// <param name="mipmap">Mipmap Level (0 = Largest)</param>
        /// <returns></returns>
        public byte[] GetTextureMipmap(int mipmap)
        {
            return GetTextureMipmapAsArray(mipmap);
        }

        /// <summary>
        /// Get a mipmap in the texture as a byte array.
        /// </summary>
        /// <param name="mipmap">Mipmap Level (0 = Largest)</param>
        /// <returns></returns>
        public byte[] GetTextureMipmapAsArray(int mipmap)
        {
            if (!InitSuccess) return null;

            int size;
            byte[] TextureMipmap = DecodeTextureMipmap(mipmap, out size);
            return ConvertRawToArray(TextureMipmap, size, size);
        }

        /// <summary>
        /// Get a mipmap in the texture as a memory stream.
        /// </summary>
        /// <param name="mipmap">Mipmap Level (0 = Largest)</param>
        /// <returns></returns>
        public MemoryStream GetTextureMipmapAsStream(int mipmap)
        {
            if (!InitSuccess) return null;

            int size;
            byte[] TextureMipmap = DecodeTextureMipmap(mipmap, out size);
            return ConvertRawToStream(TextureMipmap, size, size);
        }

        /// <summary>
        /// Get a mipmap in the texture as a System.Drawing.Bitmap object.
        /// </summary>
        /// <param name="mipmap">Mipmap Level (0 = Largest)</param>
        /// <returns></returns>
        public Bitmap GetTextureMipmapAsBitmap(int mipmap)
        {
            if (!InitSuccess) return null;

            int size;
            byte[] TextureMipmap = DecodeTextureMipmap(mipmap, out size);
            return ConvertRawToBitmap(TextureMipmap, size, size);
        }

        /// <summary>
        /// Returns if the texture contains mipmaps.
        /// </summary>
        /// <returns></returns>
        public virtual bool ContainsMipmaps()
        {
            if (!InitSuccess) return false;
            return DataCodec.ContainsMipmaps;
        }

        /// <summary>
        /// Returns the number of mipmaps in the texture, or 0 if there are none.
        /// </summary>
        /// <returns></returns>
        public int GetNumMipmaps()
        {
            if (!InitSuccess)       return 0;
            if (!ContainsMipmaps()) return 0;

            return (int)Math.Log(TextureWidth, 2) + 1;
        }
        #endregion

        #region Clut
        /// <summary>
        /// Set the clut data from an external clut file.
        /// </summary>
        /// <param name="clut">A VpClut object</param>
        public virtual void SetClut(VpClut clut)
        {
            // Should throw an ArgumentException if not the right type of
            // VrClut (ex: passing a PvpClut for a GvrTexture).

            if (!InitSuccess) return;
            if (!NeedsExternalClut()) return; // Can't use DataCodec here

            if (clut.PixelCodec != null)
                DataCodec.SetClutExternal(clut.GetClut(clut.PixelCodec), clut.GetNumClutEntries(), clut.PixelCodec);
            else
                DataCodec.SetClutExternal(clut.GetClut(PixelCodec), clut.GetNumClutEntries(), PixelCodec);
        }

        /// <summary>
        /// Returns if the texture needs an external clut file.
        /// </summary>
        /// <returns></returns>
        public virtual bool NeedsExternalClut()
        {
            if (!InitSuccess) return false;

            return DataCodec.NeedsExternalClut;
        }
        #endregion

        #region Misc
        /// <summary>
        /// Returns if the texture was loaded successfully.
        /// </summary>
        /// <returns></returns>
        public bool LoadSuccess()
        {
            return InitSuccess;
        }
        #endregion

        #region Private Properties
        // Convert raw image data to a bitmap
        private Bitmap ConvertRawToBitmap(byte[] input, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            Marshal.Copy(input, 0, bitmapData.Scan0, input.Length);
            bitmap.UnlockBits(bitmapData);

            return bitmap;
        }

        // Convert raw image data to a memory stream
        private MemoryStream ConvertRawToStream(byte[] input, int width, int height)
        {
            Bitmap bitmap       = ConvertRawToBitmap(input, width, height);
            MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);

            return stream;
        }

        // Convert raw image data to a byte array
        private byte[] ConvertRawToArray(byte[] input, int width, int height)
        {
            return ConvertRawToStream(input, width, height).ToArray();
        }

        // Decode a texture that does not contain mipmaps
        private byte[] DecodeTexture()
        {
            if (ClutOffset != -1) // The texture contains a clut
                DataCodec.SetClut(TextureData, ClutOffset, PixelCodec);

            if (ContainsMipmaps()) // If the texture contains mipmaps we have to get the largest texture
                return DataCodec.DecodeMipmap(TextureData, DataOffset, 0, TextureWidth, TextureHeight, PixelCodec);

            return DataCodec.Decode(TextureData, DataOffset, TextureWidth, TextureHeight, PixelCodec);
        }

        // Decode a texture that contains mipmaps
        private byte[] DecodeTextureMipmap(int mipmap, out int size)
        {
            if (!ContainsMipmaps()) // No mipmaps = no texture
            {
                size = 0;
                return null;
            }

            // Get the size of the mipmap
            size = TextureWidth;
            for (int i = 0; i < mipmap; i++)
                size >>= 1;
            if (size == 0) // Mipmap > number of mipmaps
                return null;

            if (ClutOffset != -1) // The texture contains a clut
                DataCodec.SetClut(TextureData, ClutOffset, PixelCodec);

            return DataCodec.DecodeMipmap(TextureData, DataOffset, mipmap, size, size, PixelCodec);
        }

        // Function for checking headers
        // Checks to see if the string matches the byte data at the specific offset
        /*
        protected static bool Compare(byte[] array, string str, int offset)
        {
            if (offset < 0 || offset + str.Length > array.Length)
                return false; // Out of bounds

            for (int i = 0; i < str.Length; i++)
            {
                if (array[offset + i] != str[i])
                    return false;
            }

            return true;
        }
         * */
        #endregion
    }
}