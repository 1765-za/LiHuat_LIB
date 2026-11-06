using System;
using System.IO;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal static class MethodBodyBlob
    {
        internal static byte[] Create(byte[] code, int maxStack, MethodBody source = null, MethodBase method = null)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            bool exceptions = source?.ExceptionHandlingClauses.Count > 0;
            writer.Write((ushort)(0x3003 | (source?.InitLocals == true ? 0x10 : 0) | (exceptions ? 8 : 0)));
            writer.Write((ushort)maxStack); writer.Write(code.Length); writer.Write(source?.LocalSignatureMetadataToken ?? 0);
            writer.Write(code);
            if (exceptions)
            {
                while ((stream.Position & 3) != 0) writer.Write((byte)0);
                int length = 4 + 24 * source.ExceptionHandlingClauses.Count;
                writer.Write((byte)0x41); writer.Write((byte)length); writer.Write((byte)(length >> 8)); writer.Write((byte)(length >> 16));
                foreach (ExceptionHandlingClause clause in source.ExceptionHandlingClauses)
                {
                    writer.Write((int)clause.Flags); writer.Write(clause.TryOffset); writer.Write(clause.TryLength);
                    writer.Write(clause.HandlerOffset); writer.Write(clause.HandlerLength);
                    writer.Write(clause.Flags == ExceptionHandlingClauseOptions.Clause ? RuntimeMethodBodies.Token(method, clause.CatchType)
                        : clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset : 0);
                }
            }
            return stream.ToArray();
        }
    }
}
