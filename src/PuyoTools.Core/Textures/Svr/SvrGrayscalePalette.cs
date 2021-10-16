﻿using System;
using System.Collections.Generic;
using System.Text;

namespace PuyoTools.Core.Textures.Svr
{
    /// <summary>
    /// A grayscale palette that can be used to decode a SVR texture when the external palette file is not known.
    /// </summary>
    public class SvrGrayscalePalette : SvrPalette
    {
        /// <summary>
        /// Throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override SvrPixelFormat PixelFormat => throw new NotSupportedException();

        /// <summary>
        /// Throws a <see cref="NotSupportedException"/>.
        /// </summary>
        /// <remarks>To get the palette data, see <see cref="GetPaletteData(PvrDataCodec)"/>.</remarks>
        public override byte[] GetPaletteData()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns a grayscale palette.
        /// </summary>
        /// <param name="dataCodec">The data codec this palette will be used for.</param>
        /// <returns>The palette data as a byte array.</returns>
        public byte[] GetPaletteData(SvrDataCodec dataCodec)
        {
            var count = dataCodec.PaletteEntries;
            var palette = new byte[count * 4];

            for (var i = 0; i < count; i++)
            {
                palette[(i * 4) + 3] = 0xFF;
                palette[(i * 4) + 2] = (byte)(i * 0xFF / count);
                palette[(i * 4) + 1] = (byte)(i * 0xFF / count);
                palette[(i * 4) + 0] = (byte)(i * 0xFF / count);
            }

            return palette;
        }
    }
}
