using System;
using Verse;

namespace LihuatLib.Experimental.VirtualEntities
{
    [Flags]
    public enum VmExperimentFeature { None = 0, Render = 1, Clr = 2, Native = 4 }
    public enum VmRouteKind { Projection, Execution }
    [Flags]
    public enum VmIntervention { None = 0, Projection = 1, Execution = 2 }

    public static class VmExperiments
    {
        private static readonly Lazy<VmExperimentFeature> enabled = new(ReadFeatures);
        public static VmExperimentFeature EnabledFeatures => enabled.Value;

        public static bool TryOpen(string owner, out VmExperimentSession session)
        {
            session = null;
            if (EnabledFeatures == VmExperimentFeature.None) return false;
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("An experiment needs an owner.", nameof(owner));
            Core.LibraryCapabilities.RequirePublicationThread();
            session = new VmExperimentSession(owner, EnabledFeatures);
            return true;
        }

        private static VmExperimentFeature ReadFeatures()
        {
            if (!GenCommandLine.TryGetCommandLineArg("lihuat-vm", out string value)) return VmExperimentFeature.None;
            VmExperimentFeature result = VmExperimentFeature.None;
            foreach (string part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                result |= part switch
                {
                    "render" => VmExperimentFeature.Render,
                    "clr" => VmExperimentFeature.Clr,
                    "native" => VmExperimentFeature.Native,
                    _ => throw new ArgumentException("Unknown VM experiment feature: " + part)
                };
            return result;
        }
    }
}
