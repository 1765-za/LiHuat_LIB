using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class NativeGenerationAdmission
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong OpenCall(ulong authority);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CloseCall(ulong authority, ulong session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PublishCall(ulong authority, ulong session, ulong revision, int blocked);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CheckCall(ulong session, ulong subject, int operation);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RememberCall(ulong authority, ulong session, ulong subject, int present);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong RecoverCall(ulong authority, ulong session, ulong subject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EndCall(ulong authority, ulong session, ulong ticket);
        private static readonly OpenCall open = NativeModule.Bind<OpenCall>("LihuatNative_AdmissionOpen");
        private static readonly CloseCall close = NativeModule.Bind<CloseCall>("LihuatNative_AdmissionClose");
        private static readonly PublishCall publish = NativeModule.Bind<PublishCall>("LihuatNative_AdmissionPublish");
        private static readonly CheckCall check = NativeModule.Bind<CheckCall>("LihuatNative_AdmissionCheck");
        private static readonly RememberCall remember = NativeModule.Bind<RememberCall>("LihuatNative_AdmissionRemember");
        private static readonly RecoverCall recover = NativeModule.Bind<RecoverCall>("LihuatNative_AdmissionRecover");
        private static readonly EndCall end = NativeModule.Bind<EndCall>("LihuatNative_AdmissionEndRecovery");

        private static void RequireCaller(bool authorized)
        {
            if (!authorized)
                throw new InvalidOperationException("Native admission mutation requires the library write gateway.");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ulong Open()
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            ulong session = open(NativeModule.Authority);
            return session != 0 ? session : throw new InvalidOperationException("Native admission session could not be created.");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Close(ulong session)
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            close(NativeModule.Authority, session);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Publish(ulong session, ulong revision, bool blocked)
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            if (publish(NativeModule.Authority, session, revision, blocked ? 1 : 0) != 1)
                throw new InvalidOperationException("Native admission snapshot rejected its session or revision.");
        }
        internal static bool Check(ulong session, ulong subject, int operation) => check(session, subject, operation) == 1;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Remember(ulong session, ulong subject, bool present)
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            return remember(NativeModule.Authority, session, subject, present ? 1 : 0) == 1;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ulong Recover(ulong session, ulong subject)
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            ulong ticket = recover(NativeModule.Authority, session, subject);
            return ticket != 0 ? ticket : throw new InvalidOperationException("Native recovery permit was rejected.");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void EndRecovery(ulong session, ulong ticket)
        {
            RequireCaller(InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeGenerationAdmission).Assembly));
            if (end(NativeModule.Authority, session, ticket) != 1)
                throw new InvalidOperationException("Recovery permits must close on their issuing thread in reverse order.");
        }
    }
}
