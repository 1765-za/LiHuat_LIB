using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCachePackage
    {
        internal const int MaximumPayload = 8 * 1024 * 1024;
        private const long Magic = 0x314843414354494C;
        private const int Published = 0x5A17CACE;
        private const int MaximumKeyBytes = 64 * 1024;
        private readonly object gate = new object();
        private readonly Dictionary<string, Entry> index = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly LifeCacheOwner owner;
        private readonly string revision;
        private string staging;
        private bool initialized;
        private long validEnd, knownLength, staleBytes;
        private int reservations;
        internal readonly string Path;
        internal string OwnerId => owner.Id;
        internal bool Used { get; private set; }

        private readonly struct Entry
        {
            internal readonly long Offset;
            internal readonly int Length, FrameLength;
            internal Entry(long offset, int length, int frameLength) { Offset = offset; Length = length; FrameLength = frameLength; }
        }

        internal LifeCachePackage(string directory, LifeCacheOwner owner, string context)
        { this.owner = owner; revision = owner.Revision + "\n" + context; Path = System.IO.Path.Combine(directory, owner.FileName); }

        private string WorkingPath => staging ?? Path;
        private static IOException Busy() => new IOException("Cache package is being prepared.", unchecked((int)0x80070020));

        internal Stream Open(string key)
        {
            lock (gate)
            {
                Initialize();
                if (!index.TryGetValue(key, out Entry entry)) return null;
                var stream = new FileStream(WorkingPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new LifeCacheSlice(stream, entry.Offset, entry.Length);
            }
        }

        internal void Accept() => Used = true;

        internal LifePreparedCacheWrite Prepare(string key, byte[] bytes)
        {
            if (bytes.Length > MaximumPayload) throw new InvalidDataException("Cache payload is too large.");
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            if (keyBytes.Length > MaximumKeyBytes) throw new InvalidDataException("Cache key is too large.");
            lock (gate)
            {
                Initialize();
                if (reservations == 0 && staleBytes >= 4 * 1024 * 1024 && staleBytes >= validEnd / 2) Compact();
                if (validEnd == 0)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    staging = Path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using var created = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    using var writer = new BinaryWriter(created, Encoding.UTF8, true);
                    WriteHeader(writer); writer.Flush();
                    knownLength = validEnd = created.Position;
                }
                using var stream = new FileStream(WorkingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                if (stream.Length != knownLength) ReadIndex(stream);
                stream.SetLength(validEnd); stream.Position = validEnd;
                using var content = new BinaryWriter(stream, Encoding.UTF8, true);
                content.Write(keyBytes.Length); content.Write(bytes.Length); content.Write(keyBytes);
                long offset = stream.Position;
                // Publication follows the coordinator's generation check; preparation only appends an uncommitted frame.
                content.Write(bytes); content.Write(0); content.Flush();
                int length = checked((int)(stream.Position - validEnd));
                knownLength = validEnd = stream.Position;
                staleBytes += length;
                Interlocked.Increment(ref reservations);
                return new LifePreparedCacheWrite(this, key, offset, bytes.Length, length);
            }
        }

        internal void Commit(LifePreparedCacheWrite write)
        {
            if (!Monitor.TryEnter(gate)) throw Busy();
            try
            {
                bool replaced = index.TryGetValue(write.Key, out Entry previous);
                if (replaced && previous.Offset > write.Offset) return;
                if (staging != null)
                {
                    if (File.Exists(Path)) File.Replace(staging, Path, null);
                    else File.Move(staging, Path);
                    staging = null;
                }
                using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.Read);
                stream.Position = write.Offset + write.Length;
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(Published); writer.Flush();
                if (replaced) staleBytes += previous.FrameLength;
                index[write.Key] = new Entry(write.Offset, write.Length, write.FrameLength);
                staleBytes -= write.FrameLength;
                Used = true;
            }
            finally { Monitor.Exit(gate); }
        }

        internal void Release() => Interlocked.Decrement(ref reservations);

        private void Initialize()
        {
            if (initialized) return;
            if (File.Exists(Path))
            {
                using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                ReadIndex(stream);
            }
            initialized = true;
        }

        private void ReadIndex(Stream stream)
        {
            index.Clear(); staleBytes = validEnd = 0; knownLength = stream.Length;
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            try
            {
                if (reader.ReadInt64() != Magic || ReadString(reader) != owner.Id || ReadString(reader) != revision) return;
                validEnd = stream.Position;
                while (stream.Length - stream.Position >= 12)
                {
                    long start = stream.Position;
                    int keyLength = reader.ReadInt32(), length = reader.ReadInt32();
                    if (keyLength <= 0 || keyLength > MaximumKeyBytes || length < 0 || length > MaximumPayload
                        || stream.Length - stream.Position < keyLength + (long)length + 4) break;
                    string key = Encoding.UTF8.GetString(reader.ReadBytes(keyLength));
                    long offset = stream.Position;
                    stream.Position += length;
                    int marker = reader.ReadInt32();
                    if (marker != 0 && marker != Published) break;
                    int frameLength = checked((int)(stream.Position - start));
                    if (marker == Published)
                    {
                        if (index.TryGetValue(key, out Entry old)) staleBytes += old.FrameLength;
                        index[key] = new Entry(offset, length, frameLength);
                    }
                    else staleBytes += frameLength;
                    validEnd = stream.Position;
                }
            }
            catch (EndOfStreamException) { }
            catch (InvalidDataException) { }
        }

        private void WriteHeader(BinaryWriter writer)
        { writer.Write(Magic); WriteString(writer, owner.Id); WriteString(writer, revision); }

        private static void WriteString(BinaryWriter writer, string value)
        { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }

        private static string ReadString(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumKeyBytes || reader.BaseStream.Length - reader.BaseStream.Position < count) throw new InvalidDataException();
            return Encoding.UTF8.GetString(reader.ReadBytes(count));
        }

        internal static string ReadOwner(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            return reader.ReadInt64() == Magic ? ReadString(reader) : null;
        }

        internal void Maintain()
        {
            lock (gate)
            {
                if (reservations != 0) return;
                if (staging != null && index.Count == 0)
                { File.Delete(staging); staging = null; validEnd = knownLength = staleBytes = 0; }
                else if (staleBytes > 0 || knownLength > validEnd) Compact();
                if (Used && File.Exists(Path)) File.SetLastWriteTimeUtc(Path, DateTime.UtcNow);
                index.Clear(); index.TrimExcess();
                initialized = false; validEnd = knownLength = staleBytes = 0;
            }
        }

        private void Compact()
        {
            if (reservations != 0 || validEnd == 0) return;
            string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
                using (var source = new FileStream(WorkingPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(target))
                {
                    WriteHeader(writer);
                    foreach (var item in index.OrderBy(e => e.Value.Offset))
                    {
                        byte[] key = Encoding.UTF8.GetBytes(item.Key);
                        writer.Write(key.Length); writer.Write(item.Value.Length); writer.Write(key); writer.Flush();
                        long offset = target.Position;
                        source.Position = item.Value.Offset;
                        Copy(source, target, item.Value.Length);
                        writer.Write(Published);
                        next.Add(item.Key, new Entry(offset, item.Value.Length, item.Value.FrameLength));
                    }
                    writer.Flush();
                }
                if (File.Exists(WorkingPath)) File.Replace(temporary, WorkingPath, null);
                else File.Move(temporary, WorkingPath);
                index.Clear(); foreach (var item in next) index.Add(item.Key, item.Value);
                knownLength = validEnd = new FileInfo(WorkingPath).Length; staleBytes = 0;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void Copy(Stream source, Stream destination, int length)
        {
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Min(65536, Math.Max(1, length)));
            try
            {
                while (length > 0)
                {
                    int count = Math.Min(buffer.Length, length);
                    source.ReadExactly(buffer.AsSpan(0, count)); destination.Write(buffer, 0, count); length -= count;
                }
            }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        }
    }
}
