using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LihuatLib.StarBlood;

namespace LihuatLib.Mechanisms
{
    internal static class LifeCachedPlans
    {
        internal static LifeAnalysisArtifact Load(LifeAnalysisKey key)
        {
            LifeAnalysisArtifact result = LifeAnalysisCoordinator.Shared?.Cache.Load(key);
            if (result == null) return null;
            var reader = new LifeIlReader(true);
            try
            {
                foreach (var input in result.Inputs)
                    if (!input.Value.Matches(new LifeMethodSnapshot(reader.ReadBody(input.Key), reader.Locals(input.Key)))) return null;
                return result;
            }
            catch (Exception) { return null; }
        }

        internal static void Store(LifeAnalysisKey key, LifeAnalysisResult result, IEnumerable<MethodInfo> methods)
        {
            LifeAnalysisDiskCache cache = LifeAnalysisCoordinator.Shared?.Cache;
            if (cache == null || StarBloodScanPolicy.CacheOnly) return;
            var reader = new LifeIlReader(true);
            var inputs = new Dictionary<MethodInfo, LifeMethodSnapshot>();
            foreach (MethodInfo method in methods.Distinct())
            {
                byte[] body;
                try { body = reader.ReadBody(method); }
                catch (Exception) { return; }
                if (body == null) return;
                inputs.Add(method, new LifeMethodSnapshot(body, reader.Locals(method)));
            }
            byte[] encoded = cache.Encode(key, new LifeAnalysisArtifact(result, inputs));
            if (encoded == null) return;
            using (LifePreparedCacheWrite write = cache.PrepareWrite(key, encoded))
                if (write != null) cache.Commit(write, true);
        }
    }
}
