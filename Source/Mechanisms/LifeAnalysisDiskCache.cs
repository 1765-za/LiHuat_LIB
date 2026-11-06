using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeAnalysisDiskCache
    {
        private const int Format = 8;
        private const int MaximumBytes = 8 * 1024 * 1024;
        private readonly LifeCacheStorage storage;
        private readonly ConcurrentDictionary<Guid, Module> modules = new ConcurrentDictionary<Guid, Module>();
        private readonly ConcurrentDictionary<string, Assembly> assemblies = new ConcurrentDictionary<string, Assembly>();
        private readonly ConcurrentDictionary<Module, Lazy<string>> hashes = new ConcurrentDictionary<Module, Lazy<string>>();
        private readonly string analyzer = typeof(LifeAnalysisDiskCache).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "LifeAnalysisPolicy").Value + ":" + LifePlatformAssemblies.PolicyKey;
        private readonly LifeAnalysisSnapshots snapshots;
        private int hits, misses, invalid, writes, failures;
        private string lastFailure;
        public int Hits => Volatile.Read(ref hits);
        public int Misses => Volatile.Read(ref misses);
        public int Invalid => Volatile.Read(ref invalid);
        public int Writes => Volatile.Read(ref writes);
        public int Failures => Volatile.Read(ref failures);
        internal string LastFailure => Volatile.Read(ref lastFailure);
        private void Failed(string stage, Exception error)
        {
            Interlocked.Increment(ref failures);
            Volatile.Write(ref lastFailure, stage + ": " + error.GetType().Name + " 0x" + error.HResult.ToString("X8") + ": " + error.Message);
        }
        public LifeAnalysisDiskCache(string directory) : this(directory, null) { }
        internal LifeAnalysisDiskCache(string directory, LifeAnalysisSnapshots snapshots)
            : this(directory, snapshots, null) { }
        internal LifeAnalysisDiskCache(string directory, LifeAnalysisSnapshots snapshots, IEnumerable<LifeCacheOwner> owners)
        { storage = new LifeCacheStorage(directory, owners, analyzer + ":" + Core.InfrastructureAssemblies.PolicyKey); this.snapshots = snapshots; RegisterAssemblies(); }
        internal Task MaintainAsync() => storage.MaintainAsync();
        internal int RemovedFiles => storage.Removed;
        internal long RemovedBytes => storage.RemovedBytes;
        internal int CleanupFailures => storage.CleanupFailures;
        public void RegisterAssemblies()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || assemblies.ContainsKey(assembly.FullName)) continue;
                assemblies.TryAdd(assembly.FullName, assembly);
                foreach (Module module in assembly.GetModules()) modules.TryAdd(module.ModuleVersionId, module);
            }
        }
        private LifeAnalysisCodec Codec() => new LifeAnalysisCodec(id => modules.TryGetValue(id, out Module module) ? module : null,
            name => assemblies.TryGetValue(name, out Assembly assembly) ? assembly : null, snapshots);
        private string FileName(LifeAnalysisKey key) => storage.Package(key).Path;
        internal void Accept(LifeAnalysisKey key) => storage.Package(key).Accept();
        internal byte[] ReadPayload(LifeAnalysisKey key)
        {
            using Stream stream = storage.Package(key).Open(key.ToString());
            if (stream == null) return null;
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
        }
        private string Hash(Module module) => hashes.GetOrAdd(module, m => new Lazy<string>(() =>
        {
            using (var stream = new FileStream(m.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", string.Empty);
        public LifeAnalysisArtifact Load(LifeAnalysisKey key)
        {
            try
            {
                using Stream stream = storage.Package(key).Open(key.ToString());
                if (stream == null) { Interlocked.Increment(ref misses); return null; }
                LifeAnalysisArtifact result = Read(key, stream);
                Accept(key); Interlocked.Increment(ref hits); return result;
            }
            catch (Exception) { Interlocked.Increment(ref invalid); return null; }
        }
        internal LifeAnalysisArtifact Decode(LifeAnalysisKey key, byte[] encoded, bool fromCache)
        {
            try
            {
                using var stream = new MemoryStream(encoded, false);
                LifeAnalysisArtifact result = Read(key, stream);
                result.Result.FromCache = fromCache;
                return result;
            }
            catch (Exception) { Interlocked.Increment(ref invalid); return null; }
        }
        private LifeAnalysisArtifact Read(LifeAnalysisKey key, Stream stream)
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            if (stream.Length < 32 || stream.Length > MaximumBytes) throw new InvalidDataException();
            byte[] expected = reader.ReadBytes(32);
            if (reader.ReadInt32() != Format || reader.ReadString() != analyzer || reader.ReadString() != key.ToString()
                || reader.ReadString() != Core.InfrastructureAssemblies.PolicyKey) throw new InvalidDataException();
            int count = LifeAnalysisCodec.Count(reader, 512);
            for (int i = 0; i < count; i++)
            {
                Guid id = Guid.ParseExact(reader.ReadString(), "N"); string hash = reader.ReadString();
                if (!modules.TryGetValue(id, out Module module) || Hash(module) != hash) throw new InvalidDataException();
            }
            long payload = stream.Position;
            stream.Position = 32;
            using (var sha = SHA256.Create())
                if (!sha.ComputeHash(stream).SequenceEqual(expected)) throw new InvalidDataException();
            stream.Position = payload;
            LifeAnalysisArtifact result = Codec().Read(reader);
            if (result.Result.Unreadable < 0 || result.Result.Limited < 0 || stream.Position != stream.Length) throw new InvalidDataException();
            return result;
        }
        public byte[] Encode(LifeAnalysisKey key, LifeAnalysisArtifact result)
        {
            if (!result.Persistable) return null;
            try
            {
                var codec = Codec();
                using (var data = new MemoryStream())
                using (var content = new BinaryWriter(data))
                using (var header = new MemoryStream())
                using (var writer = new BinaryWriter(header))
                {
                    codec.Write(content, result); content.Flush();
                    codec.Modules.Add(key.Root.Module);
                    writer.Write(Format); writer.Write(analyzer); writer.Write(key.ToString());
                    writer.Write(Core.InfrastructureAssemblies.PolicyKey); writer.Write(codec.Modules.Count);
                    foreach (Module module in codec.Modules.OrderBy(m => m.ModuleVersionId))
                    { writer.Write(module.ModuleVersionId.ToString("N")); writer.Write(Hash(module)); }
                    writer.Flush();
                    long length = 32 + header.Length + data.Length;
                    if (length > MaximumBytes) return null;
                    var encoded = new byte[(int)length];
                    Buffer.BlockCopy(header.GetBuffer(), 0, encoded, 32, (int)header.Length);
                    Buffer.BlockCopy(data.GetBuffer(), 0, encoded, 32 + (int)header.Length, (int)data.Length);
                    using (var sha = SHA256.Create())
                        Buffer.BlockCopy(sha.ComputeHash(encoded, 32, encoded.Length - 32), 0, encoded, 0, 32);
                    return encoded;
                }
            }
            catch (Exception error) { Failed("encode " + key, error); return null; }
        }
        internal LifePreparedCacheWrite PrepareWrite(LifeAnalysisKey key, byte[] bytes) => PrepareWrite(key, bytes, false);
        internal LifePreparedCacheWrite PrepareWrite(LifeAnalysisKey key, byte[] bytes, bool retry)
        {
            try
            {
                return storage.Package(key).Prepare(key.ToString(), bytes);
            }
            catch (IOException error) when (retry && Retryable(error)) { throw; }
            catch (Exception error)
            {
                Failed("prepare " + key, error);
                return null;
            }
        }
        internal void PreparationFailed(LifeAnalysisKey key, Exception error) => Failed("prepare " + key, error);
        internal bool Commit(LifePreparedCacheWrite write, bool finalAttempt)
        {
            try { write.Commit(); Interlocked.Increment(ref writes); }
            catch (IOException error) when (!finalAttempt && Retryable(error)) { return false; }
            catch (Exception error) { Failed("commit", error); }
            write.Dispose(); return true;
        }
        private static bool Retryable(IOException error)
        {
            int code = error.HResult & 0xffff;
            return code == 32 || code == 33 || code == 1175;
        }
    }
}
