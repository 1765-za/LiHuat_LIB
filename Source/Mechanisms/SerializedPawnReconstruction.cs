using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class SerializedPawnReconstruction
    {
        private static readonly HashSet<MethodInfo> examined = new HashSet<MethodInfo>();
        internal static int Installed { get; private set; }
        internal static int Rejected { get; private set; }

        internal static void Discover(MethodInfo method)
        {
            if (!method.IsStatic || method.ReturnType != typeof(bool) || method.ContainsGenericParameters) return;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string)
                || parameters[1].ParameterType != typeof(Pawn).MakeByRefType() || !parameters[1].IsOut || !examined.Add(method)) return;
            try
            {
                LifeAnalysisResult result = LifeAnalysisCoordinator.Shared?.Resolve(new LifeAnalysisKey(LifeAnalysisKind.Reconstruction, method));
                if (result?.Reconstruction != true) return;
                if (NativeIlGuards.Contains(method) || Harmony.GetPatchInfo(method)?.Owners.Any() == true) return;
                NativeIlGuards.InstallControls(new[] { method }, new[] { SerializedPawnEmitter.Build(method) });
                Installed++;
            }
            catch (Exception error)
            {
                Rejected++;
                Log.Warning("[LihuatLib.Regeneration] Snapshot reader was not guarded: " + method + ": " + error.Message);
            }
        }
        internal static RegenerationFlow.SnapshotFrame Begin() => Current.ProgramState == ProgramState.Playing
            ? LihuatGame.Current?.Regeneration.BeginSnapshot() : null;

        internal static string Inspect(string value)
        {
            RegenerationFlow flow = LihuatGame.Current?.Regeneration;
            RegenerationFlow.SnapshotFrame frame = flow?.Snapshot;
            if (frame == null) return value;
            if (frame.Denied) throw new RegenerationDeniedException();
            if (frame.Identified || string.IsNullOrEmpty(value) || value[0] != '<') return value;
            XmlElement root;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                document.LoadXml(value);
                root = document.DocumentElement;
            }
            catch (XmlException) { return value; }
            string identity = PawnIdentity(root);
            if (identity == null && root != null)
                foreach (XmlNode child in root.ChildNodes)
                {
                    identity = PawnIdentity(child as XmlElement);
                    if (identity != null) break;
                }
            if (identity != null) flow.IdentifySnapshot(identity);
            return value;
        }
        private static string PawnIdentity(XmlElement node)
        {
            string identity = node?["id"]?.InnerText;
            string defName = node?["def"]?.InnerText;
            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(defName)) return null;
            Type type = DefDatabase<ThingDef>.GetNamedSilentFail(defName)?.thingClass;
            return type != null && typeof(Pawn).IsAssignableFrom(type) ? identity : null;
        }
        internal static bool End(ref Pawn pawn, RegenerationFlow.SnapshotFrame frame)
        {
            if (frame == null || frame.Owner.EndSnapshot(frame, pawn)) return true;
            pawn = null;
            return false;
        }
    }
}
