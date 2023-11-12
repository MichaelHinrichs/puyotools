﻿using PuyoTools.Archives;
using PuyoTools.Archives.Formats.Storybook;
using PuyoTools.Core;
using PuyoTools.Core.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PuyoTools.App.Formats.Archives
{
    /// <inheritdoc/>
    internal partial class TxdStorybookFormat : IArchiveFormat
    {
        private TxdStorybookFormat() { }

        /// <summary>
        /// Gets the current instance.
        /// </summary>
        internal static TxdStorybookFormat Instance { get; } = new TxdStorybookFormat();

        public string Name => "TXD (Sonic Storybook series)";

        public string FileExtension => ".txd";

        public ArchiveBase GetCodec() => new TxdStorybookArchive();

        public ArchiveReader CreateReader(Stream source) => new TxdStorybookReader(source);

        public ArchiveWriter CreateWriter(Stream destination) => null;

        public bool Identify(Stream source, string filename) => TxdStorybookReader.IsFormat(source);
    }
}
