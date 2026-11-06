using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class ManagedIlImage
    {
        private readonly byte[] image;
        private readonly int sections;
        private readonly int sectionCount;
        private readonly int methodTable;
        private readonly int methodRowSize;
        private readonly int methodCount;
        public readonly Guid ModuleId;

        public ManagedIlImage(string path)
        {
            image = File.ReadAllBytes(path);
            int pe = I32(0x3c);
            if (I32(pe) != 0x4550) throw new BadImageFormatException();
            sectionCount = U16(pe + 6);
            int optional = pe + 24;
            sections = optional + U16(pe + 20);
            int magic = U16(optional);
            if (magic != 0x20b && magic != 0x10b) throw new BadImageFormatException();
            int directories = optional + (magic == 0x20b ? 112 : 96);
            int cli = Offset(I32(directories + 14 * 8));
            int metadata = Offset(I32(cli + 8));
            if (I32(metadata) != 0x424a5342) throw new BadImageFormatException();
            int cursor = Align(metadata + 16 + I32(metadata + 12));
            int streams = U16(cursor + 2);
            cursor += 4;
            int tables = -1, guid = -1;
            for (int i = 0; i < streams; i++)
            {
                int start = metadata + I32(cursor);
                cursor += 8;
                int name = cursor;
                while (U8(cursor++) != 0) { }
                string id = System.Text.Encoding.ASCII.GetString(image, name, cursor - name - 1);
                cursor = Align(cursor);
                if (id == "#~" || id == "#-") tables = start;
                if (id == "#GUID") guid = start;
            }
            if (tables < 0 || guid < 0) throw new BadImageFormatException();
            int heaps = U8(tables + 6);
            int strings = (heaps & 1) == 0 ? 2 : 4;
            int guids = (heaps & 2) == 0 ? 2 : 4;
            int blobs = (heaps & 4) == 0 ? 2 : 4;
            ulong valid = U64(tables + 8);
            var rows = new int[64];
            cursor = tables + 24;
            for (int i = 0; i < 64; i++) if ((valid & (1UL << i)) != 0) { rows[i] = I32(cursor); cursor += 4; }
            int moduleGuidIndex = guids == 2 ? U16(cursor + 2 + strings) : I32(cursor + 2 + strings);
            if (rows[0] != 1 || moduleGuidIndex < 1) throw new BadImageFormatException();
            var idBytes = new byte[16];
            Buffer.BlockCopy(image, guid + (moduleGuidIndex - 1) * 16, idBytes, 0, 16);
            ModuleId = new Guid(idBytes);
            var sizes = new[] {
                2 + strings + 3 * guids,
                Coded(rows, 2, 0, 26, 35, 1) + 2 * strings,
                4 + 2 * strings + Coded(rows, 2, 2, 1, 27) + Index(rows[4]) + Index(rows[6]),
                Index(rows[4]), 2 + strings + blobs, Index(rows[6])
            };
            for (int i = 0; i < 6; i++) cursor = checked(cursor + rows[i] * sizes[i]);
            methodTable = cursor;
            methodRowSize = 8 + strings + blobs + Index(rows[8]);
            methodCount = rows[6];
        }

        public byte[] Body(int token)
        {
            int row = token & 0x00ffffff;
            if ((token & unchecked((int)0xff000000)) != 0x06000000 || row < 1 || row > methodCount) return null;
            int rva = I32(checked(methodTable + (row - 1) * methodRowSize));
            if (rva == 0) return null;
            int start = Offset(rva);
            int flags = U8(start);
            int size;
            if ((flags & 3) == 2) { size = flags >> 2; start++; }
            else if ((flags & 3) == 3)
            {
                int header = (U16(start) >> 12) * 4;
                if (header < 12) throw new BadImageFormatException();
                size = I32(start + 4); start += header;
            }
            else throw new BadImageFormatException();
            if (size < 0 || start > image.Length - size) return null;
            var body = new byte[size]; Buffer.BlockCopy(image, start, body, 0, size); return body;
        }
        private int Offset(int rva)
        {
            for (int i = 0; i < sectionCount; i++)
            {
                int s = sections + i * 40;
                int begin = I32(s + 12), length = I32(s + 16);
                if (rva >= begin && rva - begin < length) return checked(I32(s + 20) + rva - begin);
            }
            throw new BadImageFormatException();
        }
        private byte U8(int at) { if (at < 0 || at >= image.Length) throw new BadImageFormatException(); return image[at]; }
        private int U16(int at) { Check(at, 2); return BitConverter.ToUInt16(image, at); }
        private int I32(int at) { Check(at, 4); return BitConverter.ToInt32(image, at); }
        private ulong U64(int at) { Check(at, 8); return BitConverter.ToUInt64(image, at); }
        private void Check(int at, int length) { if (at < 0 || at > image.Length - length) throw new BadImageFormatException(); }
        private static int Align(int value) => checked((value + 3) & ~3);
        private static int Index(int rows) => rows < 65536 ? 2 : 4;
        private static int Coded(int[] rows, int bits, params int[] tables)
        { foreach (int table in tables) if (rows[table] >= (1 << (16 - bits))) return 4; return 2; }
    }
}
